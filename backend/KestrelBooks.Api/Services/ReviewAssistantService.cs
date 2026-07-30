using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public enum FindingSeverity { Info = 0, Worth = 1, Important = 2 }

public record Finding(string Code, string Title, string Detail, FindingSeverity Severity,
    string EntityType, Guid? EntityId, decimal? Amount, DateOnly? Date);

public record ReviewResult(DateOnly From, DateOnly To, int Checked,
    List<Finding> Findings, Dictionary<string, int> Counts);

/// <summary>
/// The checklist every accountant runs in their head before signing off a period
/// — duplicate payments, VAT rates that look wrong for the supplier, round-number
/// journals, missing receipts, margin drift, dormant accounts suddenly moving —
/// done by the machine instead of by eye.
///
/// Two design commitments make this useful rather than annoying:
///
/// 1. Everything is a *question*, never an assertion. Real books contain
///    legitimate duplicates, legitimate round numbers and legitimate zero-rated
///    sales. A finding says "this is worth a look and here is why", and the
///    accountant remains the one who decides.
///
/// 2. Findings are ranked, and quiet by default. A review list of two hundred
///    items gets ignored, which is worse than no list at all — so thresholds are
///    set to surface the handful of things a reviewer would actually want to see.
/// </summary>
public class ReviewAssistantService
{
    private readonly AppDbContext _db;
    public ReviewAssistantService(AppDbContext db) => _db = db;

    public async Task<ReviewResult> ReviewAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var findings = new List<Finding>();
        var checks = 0;

        checks++; findings.AddRange(await DuplicatePurchaseInvoicesAsync(businessId, from, to));
        checks++; findings.AddRange(await DuplicatePaymentsAsync(businessId, from, to));
        checks++; findings.AddRange(await UnusualVatForSupplierAsync(businessId, from, to));
        checks++; findings.AddRange(await MissingEvidenceAsync(businessId, from, to));
        checks++; findings.AddRange(await RoundNumberJournalsAsync(businessId, from, to));
        checks++; findings.AddRange(await DormantAccountsMovingAsync(businessId, from, to));
        checks++; findings.AddRange(await MarginDriftAsync(businessId, from, to));
        checks++; findings.AddRange(await StaleDraftsAsync(businessId, to));
        checks++; findings.AddRange(await NegativeStockAsync(businessId));
        checks++; findings.AddRange(await UnreconciledBankAsync(businessId, to));

        var ordered = findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Amount ?? 0)
            .ToList();

        return new ReviewResult(from, to, checks, ordered,
            ordered.GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>Same supplier, same amount, same-ish date — the classic double entry of a bill.</summary>
    private async Task<List<Finding>> DuplicatePurchaseInvoicesAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var invoices = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date >= from && i.Date <= to)
            .Select(i => new { i.Id, i.Number, i.VendorId, Vendor = i.Vendor.Name, i.Date, i.GrossTotal })
            .ToListAsync();

        var findings = new List<Finding>();
        foreach (var group in invoices.GroupBy(i => new { i.VendorId, i.GrossTotal }))
        {
            if (group.Count() < 2) continue;
            var sorted = group.OrderBy(i => i.Date).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                var gap = sorted[i].Date.DayNumber - sorted[i - 1].Date.DayNumber;
                if (gap > 45) continue;   // a monthly bill of the same amount is normal
                findings.Add(new Finding("DUP_PURCHASE",
                    "Possible duplicate purchase invoice",
                    $"{sorted[i].Vendor}: {sorted[i - 1].Number} on {sorted[i - 1].Date:dd MMM} and "
                    + $"{sorted[i].Number} on {sorted[i].Date:dd MMM}, both £{sorted[i].GrossTotal:N2}. "
                    + "A recurring bill of the same value is normal — worth confirming these are two real bills.",
                    sorted[i].GrossTotal >= 500 ? FindingSeverity.Important : FindingSeverity.Worth,
                    "PurchaseInvoice", sorted[i].Id, sorted[i].GrossTotal, sorted[i].Date));
            }
        }
        return findings;
    }

    private async Task<List<Finding>> DuplicatePaymentsAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var payments = await _db.MoneyTransactions
            .Where(m => m.BusinessId == businessId && m.Direction == MoneyDirection.Out
                        && m.Status == DocumentStatus.Posted && m.Date >= from && m.Date <= to)
            .Select(m => new { m.Id, m.Date, m.Amount, m.Reference, m.VendorId })
            .ToListAsync();

        return payments
            .GroupBy(p => new { p.Amount, p.VendorId, p.Date })
            .Where(g => g.Count() > 1)
            .Select(g => new Finding("DUP_PAYMENT",
                "Same payment recorded more than once",
                $"{g.Count()} payments of £{g.Key.Amount:N2} on {g.Key.Date:dd MMM yyyy} to the same supplier. "
                + "If only one left the bank, one of these needs reversing.",
                FindingSeverity.Important, "MoneyTransaction", g.First().Id, g.Key.Amount, g.Key.Date))
            .ToList();
    }

    /// <summary>
    /// A supplier normally charged at 20% suddenly appearing zero-rated (or the
    /// reverse) is one of the commonest sources of an understated VAT return.
    /// </summary>
    private async Task<List<Finding>> UnusualVatForSupplierAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var lines = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted)
            .SelectMany(i => i.Lines.Select(l => new
            {
                i.Id, i.Number, i.VendorId, Vendor = i.Vendor.Name, i.Date, l.VatRate, l.Net,
            }))
            .ToListAsync();

        var findings = new List<Finding>();
        foreach (var vendorGroup in lines.GroupBy(l => l.VendorId))
        {
            var history = vendorGroup.Where(l => l.Date < from).ToList();
            if (history.Count < 3) continue;   // not enough history to have an expectation

            var usual = history.GroupBy(l => l.VatRate)
                .OrderByDescending(g => g.Count()).First();
            if (usual.Count() < history.Count * 0.8) continue;   // no consistent pattern

            foreach (var line in vendorGroup.Where(l => l.Date >= from && l.Date <= to
                                                        && l.VatRate != usual.Key)
                                            .GroupBy(l => l.Id).Select(g => g.First()))
            {
                findings.Add(new Finding("VAT_OUTLIER",
                    "VAT rate differs from this supplier's pattern",
                    $"{line.Vendor} is normally {Describe(usual.Key)} but invoice {line.Number} "
                    + $"uses {Describe(line.VatRate)}. Could be right — check the invoice.",
                    FindingSeverity.Worth, "PurchaseInvoice", line.Id, line.Net, line.Date));
            }
        }
        return findings;
    }

    /// <summary>Posted spend over the evidence threshold with nothing attached.</summary>
    private async Task<List<Finding>> MissingEvidenceAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        const decimal threshold = 100m;
        var invoices = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date >= from && i.Date <= to && i.GrossTotal >= threshold)
            .Select(i => new { i.Id, i.Number, Vendor = i.Vendor.Name, i.Date, i.GrossTotal })
            .ToListAsync();
        if (invoices.Count == 0) return new List<Finding>();

        var ids = invoices.Select(i => i.Id).ToList();
        var withAttachments = await _db.Attachments
            .Where(a => a.BusinessId == businessId
                        && a.EntityKind == AttachedTo.PurchaseInvoice && ids.Contains(a.EntityId))
            .Select(a => a.EntityId).Distinct().ToListAsync();

        return invoices.Where(i => !withAttachments.Contains(i.Id))
            .Select(i => new Finding("NO_EVIDENCE",
                "No document attached",
                $"{i.Vendor} invoice {i.Number} for £{i.GrossTotal:N2} has no supporting document. "
                + "HMRC expects the evidence to be kept for VAT recovery.",
                i.GrossTotal >= 1_000 ? FindingSeverity.Important : FindingSeverity.Worth,
                "PurchaseInvoice", i.Id, i.GrossTotal, i.Date))
            .ToList();
    }

    /// <summary>Manual journals in suspiciously round amounts — often estimates that never got revisited.</summary>
    private async Task<List<Finding>> RoundNumberJournalsAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var journals = await _db.Journals
            .Where(j => j.BusinessId == businessId && j.Status == JournalStatus.Posted
                        && j.Source == SourceType.Manual && j.Date >= from && j.Date <= to)
            .Select(j => new { j.Id, j.Number, j.Date, j.Narrative, Total = j.Lines.Sum(l => l.Debit) })
            .ToListAsync();

        return journals
            .Where(j => j.Total >= 1_000 && j.Total % 1_000 == 0)
            .Select(j => new Finding("ROUND_JOURNAL",
                "Manual journal in a round amount",
                $"Journal {j.Number} for exactly £{j.Total:N2} — \"{j.Narrative}\". "
                + "Round manual journals are often estimates; worth confirming it was replaced with the real figure.",
                FindingSeverity.Info, "JournalEntry", j.Id, j.Total, j.Date))
            .ToList();
    }

    /// <summary>An account that has been quiet for a year suddenly transacting.</summary>
    private async Task<List<Finding>> DormantAccountsMovingAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var lines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date <= to)
            .Select(l => new { l.AccountId, Code = l.Account.Code, Name = l.Account.Name,
                l.JournalEntry.Date, l.Debit, l.Credit })
            .ToListAsync();

        var findings = new List<Finding>();
        foreach (var group in lines.GroupBy(l => l.AccountId))
        {
            var inPeriod = group.Where(l => l.Date >= from).ToList();
            if (inPeriod.Count == 0) continue;
            var before = group.Where(l => l.Date < from).ToList();
            if (before.Count == 0) continue;

            var lastBefore = before.Max(l => l.Date);
            if (from.DayNumber - lastBefore.DayNumber < 365) continue;

            var amount = inPeriod.Sum(l => Math.Abs(l.Debit - l.Credit));
            if (amount < 100) continue;
            findings.Add(new Finding("DORMANT_ACCOUNT",
                "Dormant account has activity",
                $"{group.First().Code} {group.First().Name} had nothing since {lastBefore:MMM yyyy} "
                + $"and now has £{amount:N2}. Check it is coded where it belongs.",
                FindingSeverity.Worth, "Account", group.Key, amount, inPeriod.Min(l => l.Date)));
        }
        return findings;
    }

    /// <summary>Gross margin moving materially against the comparable prior period.</summary>
    private async Task<List<Finding>> MarginDriftAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        var priorFrom = from.AddDays(-days);
        var priorTo = from.AddDays(-1);

        var lines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Source != SourceType.YearEndClose
                        && l.JournalEntry.Date >= priorFrom && l.JournalEntry.Date <= to)
            .Select(l => new { l.JournalEntry.Date, Type = l.Account.Type, l.Debit, l.Credit })
            .ToListAsync();

        decimal Margin(DateOnly a, DateOnly b)
        {
            var window = lines.Where(l => l.Date >= a && l.Date <= b).ToList();
            var income = window.Where(l => l.Type == AccountType.Income).Sum(l => l.Credit - l.Debit);
            var cost = window.Where(l => l.Type == AccountType.Expense).Sum(l => l.Debit - l.Credit);
            return income == 0 ? 0 : decimal.Round((income - cost) / income * 100, 1);
        }

        var current = Margin(from, to);
        var prior = Margin(priorFrom, priorTo);
        if (prior == 0 || current == 0) return new List<Finding>();

        var swing = current - prior;
        if (Math.Abs(swing) < 10) return new List<Finding>();

        return new List<Finding>
        {
            new("MARGIN_DRIFT", "Gross margin has moved sharply",
                $"Margin is {current}% this period against {prior}% in the comparable prior period, "
                + $"a swing of {Math.Abs(swing)} points. Often a cut-off issue — an invoice or a cost "
                + "landing in the wrong period — rather than a real change in trading.",
                FindingSeverity.Important, "Report", null, null, to),
        };
    }

    private async Task<List<Finding>> StaleDraftsAsync(Guid businessId, DateOnly to)
    {
        var cutoff = to.AddDays(-30);
        var drafts = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Draft && i.Date <= cutoff)
            .Select(i => new { i.Id, i.Number, i.Date, i.GrossTotal, Customer = i.Customer.Name })
            .ToListAsync();

        return drafts.Select(d => new Finding("STALE_DRAFT",
            "Sales invoice still a draft",
            $"{d.Number} to {d.Customer} for £{d.GrossTotal:N2}, dated {d.Date:dd MMM yyyy}, "
            + "has never been posted — so it is not in the accounts and nobody is chasing it.",
            FindingSeverity.Important, "SalesInvoice", d.Id, d.GrossTotal, d.Date)).ToList();
    }

    private async Task<List<Finding>> NegativeStockAsync(Guid businessId)
    {
        var items = await _db.Items
            .Where(i => i.BusinessId == businessId && i.TrackStock && i.QuantityOnHand < 0)
            .Select(i => new { i.Id, i.Code, i.Name, i.QuantityOnHand })
            .ToListAsync();

        return items.Select(i => new Finding("NEGATIVE_STOCK",
            "Negative stock quantity",
            $"{i.Code} {i.Name} shows {i.QuantityOnHand} on hand. Usually a sale posted before "
            + "the purchase that supplied it — the stock valuation will be wrong until it is fixed.",
            FindingSeverity.Important, "Item", i.Id, null, null)).ToList();
    }

    private async Task<List<Finding>> UnreconciledBankAsync(Guid businessId, DateOnly to)
    {
        var stale = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.Status == StatementLineStatus.Unmatched
                        && l.Date <= to.AddDays(-30))
            .Select(l => new { l.BankAccountId, l.Amount })
            .ToListAsync();
        if (stale.Count == 0) return new List<Finding>();

        return new List<Finding>
        {
            new("UNRECONCILED", "Old bank lines still unmatched",
                $"{stale.Count} statement line(s) over 30 days old are still unreconciled, "
                + $"totalling £{stale.Sum(l => Math.Abs(l.Amount)):N2}. The bank balance cannot be "
                + "relied upon until these are cleared.",
                FindingSeverity.Important, "Banking", null,
                stale.Sum(l => Math.Abs(l.Amount)), null),
        };
    }

    private static string Describe(VatRate rate) => rate switch
    {
        VatRate.Standard20 => "standard-rated",
        VatRate.Reduced5 => "reduced-rated",
        VatRate.Zero => "zero-rated",
        VatRate.Exempt => "exempt",
        _ => "outside the scope",
    };
}
