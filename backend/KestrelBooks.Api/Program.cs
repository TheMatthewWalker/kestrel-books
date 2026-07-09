using System.Text;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.Password.RequiredLength = 8;
    o.Password.RequireNonAlphanumeric = false;
    o.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>();

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
builder.Services.AddSingleton<ReceiptStorageService>();
builder.Services.AddHttpClient();
if (!string.IsNullOrEmpty(builder.Configuration["Anthropic:ApiKey"]))
    builder.Services.AddScoped<IReceiptExtractor, ClaudeReceiptExtractor>();
else
    builder.Services.AddScoped<IReceiptExtractor, ManualReceiptExtractor>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Dev convenience: create the database on first run.
// For production switch to EF migrations: dotnet ef migrations add Init && dotnet ef database update
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("mobile");
app.UseAuthentication();
app.UseAuthorization();

// Friendly error surface for the domain exceptions the services throw.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "Access denied." }); }
    catch (KeyNotFoundException ex) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
});

app.MapControllers();
app.Run();
