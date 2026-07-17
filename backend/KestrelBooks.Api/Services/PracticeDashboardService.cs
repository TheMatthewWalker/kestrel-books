using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public enum DeadlineKind { VatReturn = 0, YearEnd = 1, OverdueReceivables = 2, VatRegistrationCheck = 3 }

public record Deadline(Guid BusinessId, string BusinessName, DeadlineKind Kind,
    string Title, DateOnly DueDate, int DaysUntil, string Detail, bool Actioned);

public record PracticeSummary(int ClientCount, decimal TotalReceivables, decimal TotalOverdue,
    decimal TotalPayables, int VatReturnsDueSoon, List<Deadline> Deadlines);

/// <summary>
/// The cross-client view a practitioner opens each morning: what's due, for
/// whom, and how soon. Deadlines are derived from the ledger — VAT quarter
/// ends and their filing dates (1 month + 7 days after period end, the MTD
/// rule), financial year ends, and material overdue receivables — so the
/// dashboard is useful before any client is even connected to HMRC. It spans
/// every business the practitioner can access, unlike the tenant-scoped rest
/// of the app, so it queries with filters ignored and an explicit access list.
/// </summary>
public class PracticeDashboardService
{
    private readonly AppDbContext _db;
    public PracticeDashboardService(AppDbContext db) => _db = db;

    public async Task<PracticeSummary> BuildAsync(Guid userId, DateOnly today, int horizonDays = 60)
    {
        var horizon = today.AddDays(horizonDays);

        // Every business this user can see, with their VAT scheme and year start.
        var businesses = await _db.UserBusinessAccess.IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => new
            {
                a.BusinessId, a.Business.Name, a.Business.YearStartMonth,
                a.Business.VatScheme, a.Business.LockedThrough,
                Vrn = _db.HmrcConnections.IgnoreQueryFilters()
                    .Where(h => h.BusinessId == a.BusinessId).Select(h => h.Vrn).FirstOrDefault(),
            })
            .ToListAsync();

        var deadlines = new List<Deadline>();

        foreach (var b in businesses)
        {
            // --- VAT return: current calendar quarter end + filing deadline ---
            if (b.Vrn != null)
            {
                var qEnd = NextQuarterEnd(today);
                var filingDue = qEnd.AddMonths(1).AddDays(7); // MTD: one month + 7 days
                if (filingDue <= horizon)
                {
                    var actioned = await _db.VatSubmissions.IgnoreQueryFilters()
                        .AnyAsync(v => v.BusinessId == b.BusinessId && v.PeriodTo == qEnd);
                    deadlines.Add(new Deadline(b.BusinessId, b.Name, DeadlineKind.VatReturn,
                        $"VAT return (quarter to {qEnd:dd MMM})", filingDue,
                        filingDue.DayNumber - today.DayNumber,
                        $"{b.VatScheme} scheme · file & pay by {filingDue:dd MMM yyyy}", actioned));
                }
            }

            // --- Financial year end ---
            var yearEnd = NextYearEnd(today, b.YearStartMonth);
            if (yearEnd <= horizon)
            {
                var closed = b.LockedThrough is DateOnly locked && locked >= yearEnd;
                deadlines.Add(new Deadline(b.BusinessId, b.Name, DeadlineKind.YearEnd,
                    "Financial year end", yearEnd, yearEnd.DayNumber - today.DayNumber,
                    closed ? "Year already closed & locked" : "Prepare year-end close", closed));
            }
        }

        // --- Material overdue receivables (90+ days), one line per client ---
        var accessibleIds = businesses.Select(b => b.BusinessId).ToList();
        var overdueByBiz = await OverdueReceivablesAsync(accessibleIds, today);
        foreach (var b in businesses)
        {
            if (overdueByBiz.TryGetValue(b.BusinessId, out var overdue) && overdue > 0)
                deadlines.Add(new Deadline(b.BusinessId, b.Name, DeadlineKind.OverdueReceivables,
                    "Overdue receivables (90+ days)", today, 0,
                    $"£{overdue:N2} outstanding beyond 90 days — chase", false));
        }

        var (receivables, overdueTotal) = await TotalsAsync(accessibleIds, today, sales: true);
        var (payables, _) = await TotalsAsync(accessibleIds, today, sales: false);

        return new PracticeSummary(
            businesses.Count, receivables, overdueTotal, payables,
            deadlines.Count(d => d.Kind == DeadlineKind.VatReturn && !d.Actioned),
            deadlines.OrderBy(d => d.DueDate).ThenByDescending(d => d.Kind == DeadlineKind.OverdueReceivables).ToList());
    }

    private static DateOnly NextQuarterEnd(DateOnly today)
    {
        // Calendar quarters — a reasonable default; per-business stagger groups are future work.
        var month = ((today.Month - 1) / 3 + 1) * 3;
        var end = new DateOnly(today.Year, month, 1).AddMonths(1).AddDays(-1);
        return end < today ? end.AddMonths(3) : end;
    }

    private static DateOnly NextYearEnd(DateOnly today, int yearStartMonth)
    {
        var endMonth = yearStartMonth == 1 ? 12 : yearStartMonth - 1;
        var year = today.Month > endMonth ? today.Year + 1 : today.Year;
        var end = new DateOnly(year, endMonth, 1).AddMonths(1).AddDays(-1);
        return end < today ? end.AddYears(1) : end;
    }

    private async Task<Dictionary<Guid, decimal>> OverdueReceivablesAsync(List<Guid> businessIds, DateOnly today)
    {
        var cutoff = today.AddDays(-90);
        var rows = await _db.SalesInvoices.IgnoreQueryFilters()
            .Where(i => businessIds.Contains(i.BusinessId) && i.Status == DocumentStatus.Posted
                        && i.DueDate < cutoff && i.GrossTotal - i.AmountPaid > 0)
            .Select(i => new { i.BusinessId, Outstanding = i.GrossTotal - i.AmountPaid })
            .ToListAsync();
        return rows.GroupBy(r => r.BusinessId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Outstanding));
    }

    private async Task<(decimal total, decimal overdue)> TotalsAsync(List<Guid> businessIds, DateOnly today, bool sales)
    {
        if (sales)
        {
            var rows = await _db.SalesInvoices.IgnoreQueryFilters()
                .Where(i => businessIds.Contains(i.BusinessId) && i.Status == DocumentStatus.Posted
                            && i.GrossTotal - i.AmountPaid > 0)
                .Select(i => new { i.DueDate, Outstanding = i.GrossTotal - i.AmountPaid })
                .ToListAsync();
            return (rows.Sum(r => r.Outstanding), rows.Where(r => r.DueDate < today).Sum(r => r.Outstanding));
        }
        var prows = await _db.PurchaseInvoices.IgnoreQueryFilters()
            .Where(i => businessIds.Contains(i.BusinessId) && i.Status == DocumentStatus.Posted
                        && i.GrossTotal - i.AmountPaid > 0)
            .Select(i => new { Outstanding = i.GrossTotal - i.AmountPaid })
            .ToListAsync();
        return (prows.Sum(r => r.Outstanding), 0);
    }
}
