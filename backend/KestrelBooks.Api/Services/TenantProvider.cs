using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Holds the tenant (business) for the current request. Set once by
/// TenantMiddleware after verifying the authenticated user's access.
/// AppDbContext global query filters read from this, so a query can only
/// ever see the current tenant's rows — a forgotten Where clause now
/// fails closed (returns nothing) instead of leaking another client's books.
/// </summary>
public class TenantProvider
{
    public Guid? BusinessId { get; private set; }
    public BusinessRole? Role { get; private set; }

    public void Set(Guid businessId, BusinessRole role)
    {
        BusinessId = businessId;
        Role = role;
    }
}

/// <summary>
/// Resolves {businessId} from the route, verifies the user's membership once
/// per request, and primes the TenantProvider. Requests to business-scoped
/// routes without valid membership are rejected here — controllers get an
/// additional explicit role check via AccessService for write authority.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, TenantProvider tenant, AppDbContext db)
    {
        var raw = ctx.GetRouteValue("businessId")?.ToString();
        if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var businessId))
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = 401;
                return;
            }
            var userId = AccessService.UserId(ctx.User);
            // IgnoreQueryFilters: the access table itself is the gate, not tenant-filtered.
            var access = await db.UserBusinessAccess.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.UserId == userId && a.BusinessId == businessId);
            if (access is null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(new { error = "Access denied." });
                return;
            }
            tenant.Set(businessId, access.Role);
        }
        await _next(ctx);
    }
}
