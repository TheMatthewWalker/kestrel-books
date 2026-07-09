using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record CreateBusinessRequest(string Name, string? CompanyNumber, string? VatNumber, int YearStartMonth = 4);
public record InviteUserRequest(string Email, BusinessRole Role);
public record ChangeRoleRequest(BusinessRole Role);

[ApiController]
[Authorize]
[Route("api/businesses")]
public class BusinessesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly UserManager<AppUser> _users;
    public BusinessesController(AppDbContext db, AccessService access, UserManager<AppUser> users)
    {
        _db = db; _access = access; _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var uid = AccessService.UserId(User);
        var items = await _db.UserBusinessAccess.IgnoreQueryFilters()
            .Where(a => a.UserId == uid)
            .Select(a => new { a.Business.Id, a.Business.Name, a.Business.VatNumber, a.Role })
            .OrderBy(b => b.Name)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateBusinessRequest req)
    {
        var uid = AccessService.UserId(User);
        var business = new Business
        {
            Id = Guid.NewGuid(), Name = req.Name,
            CompanyNumber = req.CompanyNumber, VatNumber = req.VatNumber,
            YearStartMonth = req.YearStartMonth
        };
        _db.Businesses.Add(business);
        _db.UserBusinessAccess.Add(new UserBusinessAccess
        {
            UserId = uid, BusinessId = business.Id, Role = BusinessRole.Owner
        });
        _db.Accounts.AddRange(CoaSeeder.DefaultChart(business.Id));
        await _db.SaveChangesAsync();
        return Ok(new { business.Id, business.Name });
    }

    // ---- Per-business user management (Owner only) ----

    [HttpGet("{businessId:guid}/users")]
    public async Task<IActionResult> Users(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        return Ok(await _db.UserBusinessAccess.IgnoreQueryFilters()
            .Where(a => a.BusinessId == businessId)
            .Select(a => new { a.UserId, a.User.Email, a.User.DisplayName, a.Role })
            .ToListAsync());
    }

    /// <summary>Grants an already-registered user access to this business.</summary>
    [HttpPost("{businessId:guid}/users")]
    public async Task<IActionResult> Invite(Guid businessId, InviteUserRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var invitee = await _users.FindByEmailAsync(req.Email);
        if (invitee is null)
            return BadRequest(new { error = "No account with that email — ask them to register first, then invite them." });
        var exists = await _db.UserBusinessAccess.IgnoreQueryFilters()
            .AnyAsync(a => a.UserId == invitee.Id && a.BusinessId == businessId);
        if (exists)
            return BadRequest(new { error = "That user already has access." });
        _db.UserBusinessAccess.Add(new UserBusinessAccess
        {
            UserId = invitee.Id, BusinessId = businessId, Role = req.Role
        });
        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(), UserId = invitee.Id, Email = invitee.Email,
            Event = AuthEventType.UserInvited, Detail = $"business {businessId} as {req.Role}",
        });
        await _db.SaveChangesAsync();
        return Ok(new { granted = true });
    }

    [HttpPut("{businessId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> ChangeRole(Guid businessId, Guid userId, ChangeRoleRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var access = await _db.UserBusinessAccess.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.BusinessId == businessId);
        if (access is null) return NotFound();
        if (access.Role == BusinessRole.Owner && req.Role != BusinessRole.Owner
            && !await HasAnotherOwnerAsync(businessId, userId))
            return BadRequest(new { error = "A business must keep at least one Owner." });
        access.Role = req.Role;
        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(), UserId = userId,
            Event = AuthEventType.RoleChanged, Detail = $"business {businessId} to {req.Role}",
        });
        await _db.SaveChangesAsync();
        return Ok(new { changed = true });
    }

    [HttpDelete("{businessId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> RemoveAccess(Guid businessId, Guid userId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var access = await _db.UserBusinessAccess.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.BusinessId == businessId);
        if (access is null) return NotFound();
        if (access.Role == BusinessRole.Owner && !await HasAnotherOwnerAsync(businessId, userId))
            return BadRequest(new { error = "A business must keep at least one Owner." });
        _db.UserBusinessAccess.Remove(access);
        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(), UserId = userId,
            Event = AuthEventType.AccessRemoved, Detail = $"business {businessId}",
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private Task<bool> HasAnotherOwnerAsync(Guid businessId, Guid exceptUserId) =>
        _db.UserBusinessAccess.IgnoreQueryFilters()
            .AnyAsync(a => a.BusinessId == businessId && a.UserId != exceptUserId
                           && a.Role == BusinessRole.Owner);
}
