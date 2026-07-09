using System.Security.Claims;
using KestrelBooks.Api.Domain;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Role authority checks for the current request. Tenant membership has
/// already been verified by TenantMiddleware (which populated TenantProvider);
/// this enforces the *level* of access an endpoint needs.
/// </summary>
public class AccessService
{
    private readonly TenantProvider _tenant;
    public AccessService(TenantProvider tenant) => _tenant = tenant;

    public static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub")
                   ?? throw new UnauthorizedAccessException());

    /// <summary>Requires the current tenant role to be at least <paramref name="minimum"/>.</summary>
    public Task EnsureAccessAsync(ClaimsPrincipal user, Guid businessId,
        BusinessRole minimum = BusinessRole.ReadOnly)
    {
        if (_tenant.BusinessId != businessId || _tenant.Role is null)
            throw new UnauthorizedAccessException("No access to this business.");
        if (BusinessRoles.Rank(_tenant.Role.Value) < BusinessRoles.Rank(minimum))
            throw new UnauthorizedAccessException($"This action requires the {minimum} role or above.");
        return Task.CompletedTask;
    }
}
