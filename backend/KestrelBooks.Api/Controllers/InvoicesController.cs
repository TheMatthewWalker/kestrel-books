using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record InvoiceLineRequest(Guid? ItemId, string Description, decimal Quantity,
    decimal UnitPrice, VatRate VatRate, Guid AccountId);
public record InvoiceRequest(Guid ContactId, string Number, DateOnly Date, DateOnly DueDate,
    string? Reference, string? Notes, List<InvoiceLineRequest> Lines);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly DocumentPostingService _docs;
    public InvoicesController(AppDbContext db, AccessService access, DocumentPostingService docs)
    {
        _db = db; _access = access; _docs = docs;
    }

    // ---- Sales ----

    [HttpGet("sales-invoices")]
    public async Task<IActionResult> SalesList(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.SalesInvoices.Where(i => i.BusinessId == businessId)
            .OrderByDescending(i => i.Date)
            .Select(i => new { i.Id, i.Number, i.Date, i.DueDate, Contact = i.Customer.Name,
                               i.NetTotal, i.VatTotal, i.GrossTotal, i.AmountPaid, i.Status })
            .ToListAsync());
    }

    [HttpGet("sales-invoices/{id:guid}")]
    public async Task<IActionResult> SalesGet(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = await _db.SalesInvoices.Include(i => i.Lines).Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == businessId);
        return inv is null ? NotFound() : Ok(inv);
    }

    [HttpPost("sales-invoices")]
    public async Task<IActionResult> SalesCreate(Guid businessId, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = new SalesInvoice { Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = req.ContactId };
        ApplySales(inv, req);
        _db.SalesInvoices.Add(inv);
        await _db.SaveChangesAsync();
        return Ok(new { inv.Id });
    }

    [HttpPut("sales-invoices/{id:guid}")]
    public async Task<IActionResult> SalesUpdate(Guid businessId, Guid id, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = await _db.SalesInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == businessId);
        if (inv is null) return NotFound();
        if (inv.Status != DocumentStatus.Draft)
            return BadRequest(new { error = "Posted invoices are locked — reverse the journal to correct." });
        inv.CustomerId = req.ContactId;
        inv.Lines.Clear();
        ApplySales(inv, req);
        await _db.SaveChangesAsync();
        return Ok(new { inv.Id });
    }

    [HttpPost("sales-invoices/{id:guid}/post")]
    public async Task<IActionResult> SalesPost(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var journal = await _docs.PostSalesInvoiceAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    // ---- Purchases ----

    [HttpGet("purchase-invoices")]
    public async Task<IActionResult> PurchaseList(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.PurchaseInvoices.Where(i => i.BusinessId == businessId)
            .OrderByDescending(i => i.Date)
            .Select(i => new { i.Id, i.Number, i.Date, i.DueDate, Contact = i.Vendor.Name,
                               i.NetTotal, i.VatTotal, i.GrossTotal, i.AmountPaid, i.Status })
            .ToListAsync());
    }

    [HttpGet("purchase-invoices/{id:guid}")]
    public async Task<IActionResult> PurchaseGet(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = await _db.PurchaseInvoices.Include(i => i.Lines).Include(i => i.Vendor)
            .FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == businessId);
        return inv is null ? NotFound() : Ok(inv);
    }

    [HttpPost("purchase-invoices")]
    public async Task<IActionResult> PurchaseCreate(Guid businessId, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = new PurchaseInvoice { Id = Guid.NewGuid(), BusinessId = businessId, VendorId = req.ContactId };
        ApplyPurchase(inv, req);
        _db.PurchaseInvoices.Add(inv);
        await _db.SaveChangesAsync();
        return Ok(new { inv.Id });
    }

    [HttpPut("purchase-invoices/{id:guid}")]
    public async Task<IActionResult> PurchaseUpdate(Guid businessId, Guid id, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var inv = await _db.PurchaseInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == businessId);
        if (inv is null) return NotFound();
        if (inv.Status != DocumentStatus.Draft)
            return BadRequest(new { error = "Posted invoices are locked — reverse the journal to correct." });
        inv.VendorId = req.ContactId;
        inv.Lines.Clear();
        ApplyPurchase(inv, req);
        await _db.SaveChangesAsync();
        return Ok(new { inv.Id });
    }

    [HttpPost("purchase-invoices/{id:guid}/post")]
    public async Task<IActionResult> PurchasePost(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var journal = await _docs.PostPurchaseInvoiceAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    private static void ApplySales(SalesInvoice inv, InvoiceRequest req)
    {
        inv.Number = req.Number; inv.Date = req.Date; inv.DueDate = req.DueDate;
        inv.Reference = req.Reference; inv.Notes = req.Notes;
        inv.Lines.AddRange(req.Lines.Select(l => new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, ItemId = l.ItemId,
            Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
            VatRate = l.VatRate, AccountId = l.AccountId
        }));
        DocumentPostingService.Recalculate(inv);
    }

    private static void ApplyPurchase(PurchaseInvoice inv, InvoiceRequest req)
    {
        inv.Number = req.Number; inv.Date = req.Date; inv.DueDate = req.DueDate;
        inv.Reference = req.Reference; inv.Notes = req.Notes;
        inv.Lines.AddRange(req.Lines.Select(l => new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id, ItemId = l.ItemId,
            Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
            VatRate = l.VatRate, AccountId = l.AccountId
        }));
        DocumentPostingService.Recalculate(inv);
    }
}
