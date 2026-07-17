using System.Text;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: JSON-friendly console output the platform (Docker,
// systemd, App Service) can collect; request logging added to the pipeline below.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Error tracking: enabled only when a DSN is configured (free tier is fine).
if (!string.IsNullOrEmpty(builder.Configuration["Sentry:Dsn"]))
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = builder.Configuration["Sentry:Dsn"];
        o.TracesSampleRate = 0.1;
    });

// Behind Caddy/nginx the client address arrives in X-Forwarded-* — needed for
// correct per-IP rate limiting and the HMRC Gov-Client-Public-IP header.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear(); // trust the reverse proxy on the private compose network
    o.KnownProxies.Clear();
});

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.Password.RequiredLength = 10;
    o.Password.RequireNonAlphanumeric = false;
    o.User.RequireUniqueEmail = true;
    o.Lockout.AllowedForNewUsers = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<PostingService>();
builder.Services.AddScoped<DocumentPostingService>();
builder.Services.AddScoped<DepreciationService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<BankImportService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<ProductionService>();
builder.Services.AddScoped<HmrcService>();
builder.Services.AddScoped<VatReturnService>();
builder.Services.AddScoped<OpeningBalanceService>();
builder.Services.AddScoped<PeriodService>();
builder.Services.AddScoped<AgedReportService>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddScoped<AttachmentService>();
// Persist Data Protection keys so encrypted secrets (TOTP, HMRC tokens) and
// MFA/state payloads survive restarts. Move to a key vault/blob in production.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "Storage", "dp-keys")));
builder.Services.AddScoped<TenantProvider>();
builder.Services.AddScoped<OneTimeCodeService>();
if (!string.IsNullOrEmpty(builder.Configuration["Smtp:Host"]))
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, LogEmailSender>();

// Rate limiting: tight window on auth (credential guessing), broad global cap.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
        ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
});
if (!string.IsNullOrEmpty(builder.Configuration["S3:Bucket"]))
    builder.Services.AddSingleton<IReceiptStorage, S3ReceiptStorage>();
else
    builder.Services.AddSingleton<IReceiptStorage, DiskReceiptStorage>();
builder.Services.AddHttpClient();
if (!string.IsNullOrEmpty(builder.Configuration["Anthropic:ApiKey"]))
    builder.Services.AddScoped<IReceiptExtractor, ClaudeReceiptExtractor>();
else
    builder.Services.AddScoped<IReceiptExtractor, ManualReceiptExtractor>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    // Authorize button in Swagger UI: paste the accessToken from /api/auth/login.
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddHostedService<AuthMaintenanceService>();
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
     .WithExposedHeaders("X-Total-Count")));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

// Apply any pending EF Core migrations on startup (creates the database if absent).
// Generate migrations with: dotnet ef migrations add <Name>   (see README).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("mobile");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Friendly error surface for the domain exceptions the services throw.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "Access denied." }); }
    catch (KeyNotFoundException ex) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "Conflict — this may already have been posted or created. Refresh and check before retrying." }); }
});

// Liveness: process is up. Readiness: process + database reachable.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

app.UseMiddleware<TenantMiddleware>();

app.MapControllers();
app.Run();
