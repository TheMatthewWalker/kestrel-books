using System.Security.Claims;
using KestrelBooks.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>Resolves the current user and enforces per-business access on every request.</summary>
public class AccessService
{
    private readonly AppDbContext _db;
    public AccessService(AppDbContext db) => _db = db;

    public static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub")
                   ?? throw new UnauthorizedAccessException());

    public async Task EnsureAccessAsync(ClaimsPrincipal user, Guid businessId)
    {
        var uid = UserId(user);
        var ok = await _db.UserBusinessAccess
            .AnyAsync(x => x.UserId == uid && x.BusinessId == businessId);
        if (!ok) throw new UnauthorizedAccessException("No access to this business.");
    }
}
