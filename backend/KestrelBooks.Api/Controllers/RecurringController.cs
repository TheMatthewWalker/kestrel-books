using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record RecurringLineDto(Guid? ItemId, string Description, decimal Quantity, decimal UnitPrice,
    VatRate VatRate, Guid AccountId);
public record RecurringRequest(Guid CustomerId, string Name, string NumberPrefix,
    RecurrenceFrequency Frequency, int PaymentTermsDays, DateOnly NextRunDate, DateOnly? EndDate,
    bool AutoPost, List<RecurringLineDto> Lines);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/recurring-invoices")]
public class RecurringController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly RecurringInvoiceService _recurring;
    public RecurringController(AppDbContext db, AccessService access, RecurringInvoiceService recurring)
    {
        _db = db; _access = access; _recurring = recurring;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.RecurringInvoices.Where(t => t.BusinessId == businessId)
            .OrderBy(t => t.NextRunDate)
            .Select(t => new
            {
                t.Id, t.Name, Customer = t.Customer.Name, t.Frequency, t.NextRunDate,
                t.EndDate, t.AutoPost, t.Paused, t.GeneratedCount, t.LastGeneratedDate
            })
            .ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, RecurringRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var t = new RecurringInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = req.CustomerId,
            Name = req.Name, NumberPrefix = string.IsNullOrWhiteSpace(req.NumberPrefix) ? "REC" : req.NumberPrefix,
            Frequency = req.Frequency, PaymentTermsDays = req.PaymentTermsDays,
            NextRunDate = req.NextRunDate, EndDate = req.EndDate, AutoPost = req.AutoPost,
        };
        foreach (var l in req.Lines)
            t.Lines.Add(new RecurringInvoiceLine
            {
                Id = Guid.NewGuid(), RecurringInvoiceId = t.Id, ItemId = l.ItemId,
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                VatRate = l.VatRate, AccountId = l.AccountId,
            });
        _db.RecurringInvoices.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid businessId, Guid id, [FromQuery] bool paused = true)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var t = await _db.RecurringInvoices.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (t is null) return NotFound();
        t.Paused = paused;
        await _db.SaveChangesAsync();
        return Ok(new { t.Paused });
    }

    /// <summary>Manual "run now" — generates anything due immediately rather than waiting for the sweep.</summary>
    [HttpPost("{id:guid}/run-now")]
    public async Task<IActionResult> RunNow(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var created = await _recurring.RunTemplateAsync(businessId, id,
            DateOnly.FromDateTime(DateTime.UtcNow), AccessService.UserId(User));
        return Ok(new { generated = created.Count, invoiceIds = created });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var t = await _db.RecurringInvoices.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (t is null) return NotFound();
        _db.RecurringInvoices.Remove(t); // generated invoices are independent and remain
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
