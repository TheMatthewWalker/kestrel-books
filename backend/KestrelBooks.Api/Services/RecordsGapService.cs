using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record MissingRecord(Guid EntityId, string Kind, string Reference, string Description,
    DateOnly Date, decimal Amount);

public record RecordsGap(int Count, decimal TotalValue, decimal RecoverableVatAtRisk,
    List<MissingRecord> Items);

/// <summary>
/// Works out precisely which transactions have no supporting document, and asks
/// the client for exactly those.
///
/// This is the inversion that makes it valuable: receipt-capture tools know what
/// the client has sent, but have no idea what the ledger is missing. Because the
/// books live here, the question becomes answerable in the useful direction —
/// not "here are some receipts" but "these fourteen payments have no evidence,
/// and £312 of VAT recovery depends on them".
/// </summary>
public class RecordsGapService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    public RecordsGapService(AppDbContext db, IEmailSender email)
    {
        _db = db; _email = email;
    }

    public async Task<RecordsGap> FindAsync(Guid businessId, DateOnly from, DateOnly to,
        decimal threshold = 50m)
    {
        var invoices = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date >= from && i.Date <= to && i.GrossTotal >= threshold)
            .Select(i => new
            {
                i.Id, i.Number, Vendor = i.Vendor.Name, i.Date, i.GrossTotal, i.VatTotal,
            })
            .ToListAsync();

        var payments = await _db.MoneyTransactions
            .Where(m => m.BusinessId == businessId && m.Status == DocumentStatus.Posted
                        && m.Direction == MoneyDirection.Out
                        && m.Date >= from && m.Date <= to && m.Amount >= threshold
                        && m.PurchaseInvoiceId == null)   // invoice-settling payments are evidenced by the invoice
            .Select(m => new { m.Id, m.Reference, m.Date, m.Amount, m.Notes })
            .ToListAsync();

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var paymentIds = payments.Select(p => p.Id).ToList();

        var attached = await _db.Attachments
            .Where(a => a.BusinessId == businessId
                        && ((a.EntityKind == AttachedTo.PurchaseInvoice && invoiceIds.Contains(a.EntityId))
                            || (a.EntityKind == AttachedTo.MoneyTransaction && paymentIds.Contains(a.EntityId))))
            .Select(a => a.EntityId)
            .Distinct()
            .ToListAsync();
        var attachedSet = attached.ToHashSet();

        var items = new List<MissingRecord>();
        var vatAtRisk = 0m;

        foreach (var i in invoices.Where(i => !attachedSet.Contains(i.Id)))
        {
            items.Add(new MissingRecord(i.Id, "Purchase invoice", i.Number,
                $"{i.Vendor} — £{i.GrossTotal:N2}", i.Date, i.GrossTotal));
            vatAtRisk += i.VatTotal;
        }
        foreach (var p in payments.Where(p => !attachedSet.Contains(p.Id)))
            items.Add(new MissingRecord(p.Id, "Payment", p.Reference,
                p.Notes ?? "Payment from the bank", p.Date, p.Amount));

        var ordered = items.OrderByDescending(i => i.Amount).ToList();
        return new RecordsGap(ordered.Count, ordered.Sum(i => i.Amount),
            decimal.Round(vatAtRisk, 2), ordered);
    }

    /// <summary>
    /// Emails the client the specific list. Deliberately itemised rather than a
    /// vague "please send your receipts" — the whole point is that the client is
    /// told exactly what is outstanding, which is the difference between a
    /// request that gets actioned and one that gets ignored.
    /// </summary>
    public async Task<int> RequestAsync(Guid businessId, string toEmail, RecordsGap gap)
    {
        if (gap.Count == 0) return 0;
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);

        var lines = string.Join("\n", gap.Items.Take(100).Select(i =>
            $"  · {i.Date:dd MMM yyyy}  {i.Reference,-14} {i.Description}"));

        var body =
            $"Hello,\n\nWe are missing the paperwork behind {gap.Count} transaction(s) on "
          + $"{business.Name}'s records, totalling £{gap.TotalValue:N2}.\n\n"
          + (gap.RecoverableVatAtRisk > 0
              ? $"About £{gap.RecoverableVatAtRisk:N2} of VAT recovery depends on having these, "
              + "so they are worth digging out.\n\n"
              : "")
          + $"{lines}\n\n"
          + "A photo of each is fine — they do not need to be tidy.\n\nThank you.";

        await _email.SendAsync(toEmail,
            $"{business.Name}: {gap.Count} missing record(s)", body);
        return gap.Count;
    }
}
