using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record ItemRequest(ItemKind Kind, string Code, string Name, decimal SalesPrice,
    decimal PurchasePrice, VatRate DefaultVatRate, Guid? SalesAccountId, Guid? PurchaseAccountId);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/items")]
public class ItemsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    public ItemsController(AppDbContext db, AccessService access) { _db = db; _access = access; }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Items.Where(i => i.BusinessId == businessId && !i.Archived)
            .OrderBy(i => i.Code).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, ItemRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var item = new Item { Id = Guid.NewGuid(), BusinessId = businessId };
        Apply(item, req);
        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid id, ItemRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == businessId);
        if (item is null) return NotFound();
        Apply(item, req);
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    private static void Apply(Item i, ItemRequest req)
    {
        i.Kind = req.Kind; i.Code = req.Code; i.Name = req.Name;
        i.SalesPrice = req.SalesPrice; i.PurchasePrice = req.PurchasePrice;
        i.DefaultVatRate = req.DefaultVatRate;
        i.SalesAccountId = req.SalesAccountId; i.PurchaseAccountId = req.PurchaseAccountId;
    }
}
