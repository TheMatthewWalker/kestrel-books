using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record AllocateRequest(Guid InvoiceId, decimal Amount);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class CreditNotesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly DocumentPostingService _docs;
    public CreditNotesController(AppDbContext db, AccessService access, DocumentPostingService docs)
    {
        _db = db; _access = access; _docs = docs;
    }

    // ---- Sales credit notes ----

    [HttpGet("sales-credit-notes")]
    public async Task<IActionResult> SalesList(Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);
        var query = _db.SalesCreditNotes.Where(c => c.BusinessId == businessId);
        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        return Ok(await query.OrderByDescending(c => c.Date).Skip(skip).Take(take)
            .Select(c => new { c.Id, c.Number, c.Date, Contact = c.Customer.Name,
                               c.NetTotal, c.VatTotal, c.GrossTotal, c.AmountPaid, c.Status })
            .ToListAsync());
    }

    [HttpPost("sales-credit-notes")]
    public async Task<IActionResult> SalesCreate(Guid businessId, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var cn = new SalesCreditNote { Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = req.ContactId };
        Apply(cn, req, cn.Lines, l => new SalesCreditNoteLine { SalesCreditNoteId = cn.Id });
        _db.SalesCreditNotes.Add(cn);
        await _db.SaveChangesAsync();
        return Ok(new { cn.Id });
    }

    [HttpPost("sales-credit-notes/{id:guid}/post")]
    public async Task<IActionResult> SalesPost(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _docs.PostSalesCreditNoteAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    /// <summary>
    /// Allocates a posted credit note against a posted invoice for the same
    /// customer. No journal — both live in the debtors control account; this
    /// is a contra that reduces each document's outstanding balance.
    /// </summary>
    [HttpPost("sales-credit-notes/{id:guid}/allocate")]
    public async Task<IActionResult> SalesAllocate(Guid businessId, Guid id, AllocateRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var cn = await _db.SalesCreditNotes.FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId);
        var inv = await _db.SalesInvoices.FirstOrDefaultAsync(i => i.Id == req.InvoiceId && i.BusinessId == businessId);
        if (cn is null || inv is null) return NotFound();
        if (cn.Status != DocumentStatus.Posted || inv.Status != DocumentStatus.Posted)
            return BadRequest(new { error = "Both documents must be posted before allocating." });
        if (cn.CustomerId != inv.CustomerId)
            return BadRequest(new { error = "Credit note and invoice belong to different customers." });
        if (req.Amount <= 0
            || req.Amount > cn.GrossTotal - cn.AmountPaid
            || req.Amount > inv.GrossTotal - inv.AmountPaid)
            return BadRequest(new { error = "Allocation exceeds an outstanding balance." });

        cn.AmountPaid += req.Amount;
        inv.AmountPaid += req.Amount;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            creditNoteRemaining = cn.GrossTotal - cn.AmountPaid,
            invoiceOutstanding = inv.GrossTotal - inv.AmountPaid,
        });
    }

    // ---- Purchase credit notes ----

    [HttpGet("purchase-credit-notes")]
    public async Task<IActionResult> PurchaseList(Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);
        var query = _db.PurchaseCreditNotes.Where(c => c.BusinessId == businessId);
        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        return Ok(await query.OrderByDescending(c => c.Date).Skip(skip).Take(take)
            .Select(c => new { c.Id, c.Number, c.Date, Contact = c.Vendor.Name,
                               c.NetTotal, c.VatTotal, c.GrossTotal, c.AmountPaid, c.Status })
            .ToListAsync());
    }

    [HttpPost("purchase-credit-notes")]
    public async Task<IActionResult> PurchaseCreate(Guid businessId, InvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var cn = new PurchaseCreditNote { Id = Guid.NewGuid(), BusinessId = businessId, VendorId = req.ContactId };
        Apply(cn, req, cn.Lines, l => new PurchaseCreditNoteLine { PurchaseCreditNoteId = cn.Id });
        _db.PurchaseCreditNotes.Add(cn);
        await _db.SaveChangesAsync();
        return Ok(new { cn.Id });
    }

    [HttpPost("purchase-credit-notes/{id:guid}/post")]
    public async Task<IActionResult> PurchasePost(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _docs.PostPurchaseCreditNoteAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    [HttpPost("purchase-credit-notes/{id:guid}/allocate")]
    public async Task<IActionResult> PurchaseAllocate(Guid businessId, Guid id, AllocateRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var cn = await _db.PurchaseCreditNotes.FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId);
        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == req.InvoiceId && i.BusinessId == businessId);
        if (cn is null || inv is null) return NotFound();
        if (cn.Status != DocumentStatus.Posted || inv.Status != DocumentStatus.Posted)
            return BadRequest(new { error = "Both documents must be posted before allocating." });
        if (cn.VendorId != inv.VendorId)
            return BadRequest(new { error = "Credit note and invoice belong to different suppliers." });
        if (req.Amount <= 0
            || req.Amount > cn.GrossTotal - cn.AmountPaid
            || req.Amount > inv.GrossTotal - inv.AmountPaid)
            return BadRequest(new { error = "Allocation exceeds an outstanding balance." });

        cn.AmountPaid += req.Amount;
        inv.AmountPaid += req.Amount;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            creditNoteRemaining = cn.GrossTotal - cn.AmountPaid,
            invoiceOutstanding = inv.GrossTotal - inv.AmountPaid,
        });
    }

    private static void Apply<TLine>(InvoiceBase doc, InvoiceRequest req, List<TLine> target,
        Func<InvoiceLineRequest, TLine> factory) where TLine : InvoiceLineBase
    {
        doc.Number = req.Number; doc.Date = req.Date; doc.DueDate = req.DueDate;
        doc.Reference = req.Reference; doc.Notes = req.Notes;
        foreach (var l in req.Lines)
        {
            var line = factory(l);
            line.Id = Guid.NewGuid();
            line.ItemId = l.ItemId; line.Description = l.Description;
            line.Quantity = l.Quantity; line.UnitPrice = l.UnitPrice;
            line.VatRate = l.VatRate; line.AccountId = l.AccountId;
            target.Add(line);
        }
        DocumentPostingService.Recalculate(doc);
    }
}
