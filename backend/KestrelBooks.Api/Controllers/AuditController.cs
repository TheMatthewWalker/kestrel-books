using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/audit")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    public AuditController(AppDbContext db, AccessService access)
    {
        _db = db; _access = access;
    }

    /// <summary>
    /// The change log. Filterable by record so "who changed this invoice?" is one
    /// query, or unfiltered as a recent-activity feed for the client.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid businessId,
        [FromQuery] string? entityType, [FromQuery] Guid? entityId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        // Reading the audit trail is an oversight function, not a bookkeeping one.
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);

        var query = _db.AuditEntries.Where(a => a.BusinessId == businessId);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (entityId is Guid id) query = query.Where(a => a.EntityId == id);
        if (from is DateTime f) query = query.Where(a => a.AtUtc >= f);
        if (to is DateTime t) query = query.Where(a => a.AtUtc <= t);

        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        var rows = await query.OrderByDescending(a => a.AtUtc).Skip(skip).Take(take).ToListAsync();
        return Ok(rows.Select(a => new
        {
            a.Id, a.EntityType, a.EntityId, a.Action, a.Changes, a.UserName, a.UserId, a.AtUtc,
        }));
    }
}
