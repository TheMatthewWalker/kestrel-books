using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record BomLineRequest(Guid ComponentItemId, decimal QuantityPer);
public record BomRequest(decimal LabourCostPerUnit, decimal OverheadCostPerUnit, List<BomLineRequest> Lines);
public record CreateOrderRequest(Guid ItemId, decimal Quantity, string? Notes);
public record CompleteOrderRequest(DateOnly Date, decimal QuantityCompleted);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/production")]
public class ProductionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly ProductionService _production;
    public ProductionController(AppDbContext db, AccessService access, ProductionService production)
    {
        _db = db; _access = access; _production = production;
    }

    // ---- Bill of materials ----

    [HttpGet("boms/{parentItemId:guid}")]
    public async Task<IActionResult> GetBom(Guid businessId, Guid parentItemId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var bom = await _db.BillOfMaterials.Include(b => b.Lines).ThenInclude(l => l.ComponentItem)
            .FirstOrDefaultAsync(b => b.BusinessId == businessId && b.ParentItemId == parentItemId);
        if (bom is null) return Ok(new { exists = false });
        return Ok(new
        {
            exists = true, bom.LabourCostPerUnit, bom.OverheadCostPerUnit,
            lines = bom.Lines.Select(l => new
            {
                l.ComponentItemId, Code = l.ComponentItem.Code, Name = l.ComponentItem.Name, l.QuantityPer
            })
        });
    }

    [HttpPut("boms/{parentItemId:guid}")]
    public async Task<IActionResult> SetBom(Guid businessId, Guid parentItemId, BomRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (req.Lines.Any(l => l.ComponentItemId == parentItemId))
            return BadRequest(new { error = "An item cannot be a component of itself." });
        var bom = await _db.BillOfMaterials.Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.BusinessId == businessId && b.ParentItemId == parentItemId);
        if (bom is null)
        {
            bom = new BillOfMaterial { Id = Guid.NewGuid(), BusinessId = businessId, ParentItemId = parentItemId };
            _db.BillOfMaterials.Add(bom);
        }
        bom.LabourCostPerUnit = req.LabourCostPerUnit;
        bom.OverheadCostPerUnit = req.OverheadCostPerUnit;
        bom.Lines.Clear();
        bom.Lines.AddRange(req.Lines.Select(l => new BomLine
        {
            Id = Guid.NewGuid(), BillOfMaterialId = bom.Id,
            ComponentItemId = l.ComponentItemId, QuantityPer = l.QuantityPer,
        }));
        await _db.SaveChangesAsync();
        return Ok(new { saved = true });
    }

    // ---- Works orders ----

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.ProductionOrders
            .Where(o => o.BusinessId == businessId)
            .OrderByDescending(o => o.CreatedDate)
            .Take(200)
            .Select(o => new
            {
                o.Id, o.Number, ItemCode = o.Item.Code, ItemName = o.Item.Name,
                o.QuantityPlanned, o.QuantityCompleted, o.Status,
                o.MaterialCost, o.LabourCost, o.OverheadCost,
                TotalCost = o.MaterialCost + o.LabourCost + o.OverheadCost,
            })
            .ToListAsync());
    }

    [HttpPost("orders")]
    public async Task<IActionResult> Create(Guid businessId, CreateOrderRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var order = await _production.CreateAsync(businessId, req.ItemId, req.Quantity, req.Notes);
        return Ok(new { order.Id, order.Number });
    }

    [HttpPost("orders/{id:guid}/issue-materials")]
    public async Task<IActionResult> Issue(Guid businessId, Guid id, [FromQuery] DateOnly? date)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _production.IssueMaterialsAsync(businessId, id,
            date ?? DateOnly.FromDateTime(DateTime.Today), AccessService.UserId(User));
        return Ok(new { journalNumber = journal.Number });
    }

    [HttpPost("orders/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid businessId, Guid id, CompleteOrderRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _production.CompleteAsync(businessId, id, req.Date,
            req.QuantityCompleted, AccessService.UserId(User));
        return Ok(new { journalNumber = journal.Number });
    }
}
