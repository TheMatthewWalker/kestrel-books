using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record AdjustStockRequest(Guid ItemId, DateOnly Date, decimal Quantity, decimal? UnitCost, string Reason);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/inventory")]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly StockService _stock;
    public InventoryController(AppDbContext db, AccessService access, StockService stock)
    {
        _db = db; _access = access; _stock = stock;
    }

    /// <summary>Creates the manufacturing accounts (RM/WIP/FG, COGS, absorption) if missing — idempotent.</summary>
    [HttpPost("enable")]
    public async Task<IActionResult> Enable(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        await _stock.EnsureManufacturingAccountsAsync(businessId);
        return Ok(new { enabled = true });
    }

    /// <summary>Stock levels: quantity on hand, AVCO and value per tracked item.</summary>
    [HttpGet("levels")]
    public async Task<IActionResult> Levels(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var items = await _db.Items
            .Where(i => i.BusinessId == businessId && i.TrackStock && !i.Archived)
            .OrderBy(i => i.Code)
            .Select(i => new
            {
                i.Id, i.Code, i.Name, i.Kind, i.QuantityOnHand, i.AvgUnitCost,
                Value = Math.Round(i.QuantityOnHand * i.AvgUnitCost, 2)
            })
            .ToListAsync();
        return Ok(new { items, totalValue = items.Sum(i => i.Value) });
    }

    /// <summary>Movement history (item card) for one item.</summary>
    [HttpGet("movements/{itemId:guid}")]
    public async Task<IActionResult> Movements(Guid businessId, Guid itemId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);
        var query = _db.StockMovements.Where(m => m.BusinessId == businessId && m.ItemId == itemId);
        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        return Ok(await query
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.CreatedAtUtc)
            .Skip(skip).Take(take)
            .Select(m => new { m.Id, m.Date, m.Type, m.Quantity, m.UnitCost, m.Value, m.QuantityAfter, m.Notes })
            .ToListAsync());
    }

    /// <summary>Stock count adjustment: positive writes up, negative writes off. Posts the journal.</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust(Guid businessId, AdjustStockRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _stock.AdjustAsync(businessId, req.ItemId, req.Date,
            req.Quantity, req.UnitCost, req.Reason, AccessService.UserId(User));
        return Ok(new { journalNumber = journal.Number });
    }
}
