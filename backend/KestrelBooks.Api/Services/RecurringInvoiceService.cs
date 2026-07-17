using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public class RecurringInvoiceService
{
    private readonly AppDbContext _db;
    private readonly DocumentPostingService _docs;
    private readonly TenantProvider _tenant;
    public RecurringInvoiceService(AppDbContext db, DocumentPostingService docs, TenantProvider tenant)
    {
        _db = db; _docs = docs; _tenant = tenant;
    }

    public static DateOnly Advance(DateOnly from, RecurrenceFrequency freq) => freq switch
    {
        RecurrenceFrequency.Weekly => from.AddDays(7),
        RecurrenceFrequency.Monthly => from.AddMonths(1),
        RecurrenceFrequency.Quarterly => from.AddMonths(3),
        RecurrenceFrequency.Yearly => from.AddYears(1),
        _ => from.AddMonths(1),
    };

    /// <summary>
    /// Generates every invoice now due for one template (catching up if the
    /// generator hasn't run for several periods), advancing the schedule each
    /// time. Returns the created invoice ids. Stops at EndDate or when paused.
    /// </summary>
    public async Task<List<Guid>> RunTemplateAsync(Guid businessId, Guid templateId, DateOnly today, Guid userId)
    {
        var t = await _db.RecurringInvoices.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Template not found.");

        var created = new List<Guid>();
        if (t.Paused) return created;

        while (t.NextRunDate <= today && (t.EndDate is null || t.NextRunDate <= t.EndDate))
        {
            var issueDate = t.NextRunDate;
            var invoice = new SalesInvoice
            {
                Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = t.CustomerId,
                Number = $"{t.NumberPrefix}-{t.NextNumber:0000}",
                Date = issueDate, DueDate = issueDate.AddDays(t.PaymentTermsDays),
                Notes = $"Generated from recurring template \"{t.Name}\"",
            };
            foreach (var l in t.Lines)
                invoice.Lines.Add(new SalesInvoiceLine
                {
                    Id = Guid.NewGuid(), SalesInvoiceId = invoice.Id, ItemId = l.ItemId,
                    Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                    VatRate = l.VatRate, AccountId = l.AccountId,
                });
            DocumentPostingService.Recalculate(invoice);
            _db.SalesInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            if (t.AutoPost)
                await _docs.PostSalesInvoiceAsync(businessId, invoice.Id, userId);

            created.Add(invoice.Id);
            t.NextNumber++;
            t.GeneratedCount++;
            t.LastGeneratedDate = issueDate;
            t.NextRunDate = Advance(t.NextRunDate, t.Frequency);
            await _db.SaveChangesAsync();
        }
        return created;
    }

    /// <summary>Sweeps all businesses' due templates. Called by the background generator.</summary>
    public async Task<int> RunAllDueAsync(DateOnly today, Guid systemUserId)
    {
        // IgnoreQueryFilters: the generator runs outside any tenant/request scope.
        var due = await _db.RecurringInvoices.IgnoreQueryFilters()
            .Where(t => !t.Paused && t.NextRunDate <= today
                        && (t.EndDate == null || t.NextRunDate <= t.EndDate))
            .Select(t => new { t.Id, t.BusinessId })
            .ToListAsync();

        var total = 0;
        foreach (var t in due)
        {
            // Prime the tenant so RunTemplateAsync's fail-closed query filters
            // see this business's rows (the generator runs outside a request).
            _tenant.Set(t.BusinessId, BusinessRole.Owner);
            total += (await RunTemplateAsync(t.BusinessId, t.Id, today, systemUserId)).Count;
        }
        return total;
    }
}
