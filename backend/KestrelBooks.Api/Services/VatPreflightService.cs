using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record PreflightCheck(string Code, string Title, string Detail, FindingSeverity Severity, bool Passed);

public record VatPreflight(DateOnly From, DateOnly To, VatBoxes Boxes,
    List<PreflightCheck> Checks, int Failures, bool SafeToSubmit);

/// <summary>
/// Sanity checks a VAT return against the client's own history before it is
/// filed. HMRC penalties are real and a return cannot be unfiled — so the few
/// seconds this takes are worth it every single time.
///
/// Every check compares the return to what this business normally does rather
/// than to a universal rule, because "normal" varies enormously: a zero-rated
/// food wholesaler and a standard-rated consultancy would each look alarming
/// measured against the other.
/// </summary>
public class VatPreflightService
{
    private readonly AppDbContext _db;
    private readonly VatReturnService _vat;
    public VatPreflightService(AppDbContext db, VatReturnService vat)
    {
        _db = db; _vat = vat;
    }

    public async Task<VatPreflight> CheckAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var boxes = await _vat.ComputeAsync(businessId, from, to);
        var checks = new List<PreflightCheck>();

        // --- Has this period already been filed? ---
        var alreadyFiled = await _db.VatSubmissions
            .AnyAsync(v => v.BusinessId == businessId && v.PeriodFrom == from && v.PeriodTo == to);
        checks.Add(new PreflightCheck("ALREADY_FILED", "Period not already submitted",
            alreadyFiled
                ? "A return has already been submitted for exactly this period. Filing again would duplicate it."
                : "No existing submission covers this period.",
            FindingSeverity.Important, !alreadyFiled));

        // --- Does it overlap a filed period? ---
        var overlaps = await _db.VatSubmissions
            .Where(v => v.BusinessId == businessId && v.PeriodFrom <= to && v.PeriodTo >= from)
            .Select(v => new { v.PeriodFrom, v.PeriodTo })
            .ToListAsync();
        var overlapping = overlaps.Where(o => !(o.PeriodFrom == from && o.PeriodTo == to)).ToList();
        checks.Add(new PreflightCheck("OVERLAP", "No overlap with a filed period",
            overlapping.Count > 0
                ? $"This period overlaps {overlapping.Count} already-filed return(s) — transactions would be counted twice."
                : "The period sits cleanly after previous returns.",
            FindingSeverity.Important, overlapping.Count == 0));

        // --- Is there a gap since the last return? ---
        var lastFiled = await _db.VatSubmissions
            .Where(v => v.BusinessId == businessId && v.PeriodTo < from)
            .OrderByDescending(v => v.PeriodTo)
            .Select(v => (DateOnly?)v.PeriodTo)
            .FirstOrDefaultAsync();
        var gapDays = lastFiled is DateOnly last ? from.DayNumber - last.DayNumber - 1 : 0;
        checks.Add(new PreflightCheck("GAP", "No gap since the last return",
            gapDays > 0
                ? $"There is a {gapDays}-day gap between the last filed period and this one. "
                  + "Any transactions in that gap would never be reported."
                : "This period follows straight on from the last one filed.",
            FindingSeverity.Important, gapDays <= 0));

        // --- Unposted drafts inside the period ---
        var draftSales = await _db.SalesInvoices.CountAsync(i => i.BusinessId == businessId
            && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to);
        var draftPurchases = await _db.PurchaseInvoices.CountAsync(i => i.BusinessId == businessId
            && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to);
        var drafts = draftSales + draftPurchases;
        checks.Add(new PreflightCheck("DRAFTS", "Nothing left in draft",
            drafts > 0
                ? $"{drafts} invoice(s) dated in this period are still drafts, so they are excluded "
                  + "from these figures. Post them or accept they belong to a later return."
                : "Every invoice in the period is posted.",
            FindingSeverity.Important, drafts == 0));

        // --- Unreconciled bank lines in the period (cash scheme especially) ---
        var unmatched = await _db.BankStatementLines.CountAsync(l => l.BusinessId == businessId
            && l.Status == StatementLineStatus.Unmatched && l.Date >= from && l.Date <= to);
        checks.Add(new PreflightCheck("BANK", "Bank reconciled for the period",
            unmatched > 0
                ? $"{unmatched} bank line(s) in the period are unreconciled. Some may be purchases "
                  + "with recoverable VAT that is missing from box 4."
                : "No unreconciled bank lines in the period.",
            unmatched > 5 ? FindingSeverity.Important : FindingSeverity.Worth, unmatched == 0));

        // --- Ratio checks against this client's own history ---
        var history = await _db.VatSubmissions
            .Where(v => v.BusinessId == businessId && v.PeriodTo < from)
            .OrderByDescending(v => v.PeriodTo).Take(4)
            .Select(v => v.BoxesJson).ToListAsync();

        if (history.Count >= 2)
        {
            var priorRates = new List<decimal>();
            var priorNets = new List<decimal>();
            foreach (var json in history)
            {
                try
                {
                    var prior = System.Text.Json.JsonSerializer.Deserialize<VatBoxes>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (prior is null) continue;
                    if (prior.TotalValueSalesExVAT > 0)
                        priorRates.Add(prior.VatDueSales / prior.TotalValueSalesExVAT * 100);
                    priorNets.Add(prior.NetVatDue);
                }
                catch { /* a malformed historical record shouldn't block a filing */ }
            }

            if (priorRates.Count >= 2 && boxes.TotalValueSalesExVAT > 0)
            {
                var currentRate = boxes.VatDueSales / boxes.TotalValueSalesExVAT * 100;
                var averageRate = priorRates.Average();
                var drift = Math.Abs(currentRate - averageRate);
                checks.Add(new PreflightCheck("RATE_DRIFT", "Effective VAT rate in line with history",
                    drift > 5
                        ? $"Output VAT is {currentRate:N1}% of net sales this period against "
                          + $"{averageRate:N1}% historically. Often a VAT rate coded wrongly, or a "
                          + "genuine change in the sales mix."
                        : $"Effective rate {currentRate:N1}%, consistent with the usual {averageRate:N1}%.",
                    FindingSeverity.Worth, drift <= 5));
            }

            if (priorNets.Count >= 2 && priorNets.Any(n => n != 0))
            {
                var averageNet = priorNets.Where(n => n != 0).Average(Math.Abs);
                if (averageNet > 0)
                {
                    var ratio = Math.Abs(boxes.NetVatDue) / averageNet;
                    checks.Add(new PreflightCheck("SIZE", "Return a normal size for this client",
                        ratio > 3 || ratio < 0.33m
                            ? $"Net VAT due of £{boxes.NetVatDue:N2} is well outside the usual "
                              + $"£{averageNet:N2}. Worth understanding why before it goes."
                            : $"£{boxes.NetVatDue:N2}, in line with the usual £{averageNet:N2}.",
                        FindingSeverity.Worth, ratio is <= 3 and >= 0.33m));
                }
            }
        }

        // --- Internal arithmetic ---
        var box3 = boxes.VatDueSales + boxes.VatDueAcquisitions;
        var box5 = box3 - boxes.VatReclaimedCurrPeriod;
        var arithmeticOk = Math.Abs(box3 - boxes.TotalVatDue) < 0.02m
                           && Math.Abs(box5 - boxes.NetVatDue) < 0.02m;
        checks.Add(new PreflightCheck("ARITHMETIC", "Boxes add up",
            arithmeticOk
                ? "Box 3 equals boxes 1 and 2, and box 5 equals box 3 less box 4."
                : "The boxes do not add up internally — do not submit this.",
            FindingSeverity.Important, arithmeticOk));

        // --- Nil return sanity ---
        var nilButTrading = boxes.TotalVatDue == 0 && boxes.VatReclaimedCurrPeriod == 0
                            && (boxes.TotalValueSalesExVAT > 0 || boxes.TotalValuePurchasesExVAT > 0);
        checks.Add(new PreflightCheck("NIL", "Nil return looks deliberate",
            nilButTrading
                ? "No VAT either way, yet there is trading activity in the period. Check the VAT "
                  + "codes — a whole period coded outside the scope is a common mistake."
                : "Nothing inconsistent about the VAT totals.",
            FindingSeverity.Worth, !nilButTrading));

        var failures = checks.Count(c => !c.Passed);
        var blocking = checks.Any(c => !c.Passed && c.Severity == FindingSeverity.Important);

        return new VatPreflight(from, to, boxes, checks, failures, !blocking);
    }
}
