using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record ReportLine(string Code, string Name, string Group, decimal Amount);
public record ReportResult(string Title, string Period, List<ReportSection> Sections, decimal Total);
public record ReportSection(string Name, List<ReportLine> Lines, decimal Subtotal);

/// <summary>
/// Live reporting straight off posted journals — no period-close batch needed.
/// Sign convention: debit-normal balances (assets, expenses) are positive as debits;
/// credit-normal balances (liabilities, equity, income) are shown positive as credits.
/// </summary>
public class ReportService
{
    private readonly AppDbContext _db;
    public ReportService(AppDbContext db) => _db = db;

    private IQueryable<JournalLine> PostedLines(Guid businessId) =>
        _db.JournalLines.Where(l =>
            l.JournalEntry.BusinessId == businessId &&
            (l.JournalEntry.Status == JournalStatus.Posted || l.JournalEntry.Status == JournalStatus.Reversed));

    public async Task<ReportResult> TrialBalanceAsync(Guid businessId, DateOnly asOf)
    {
        var rows = await PostedLines(businessId)
            .Where(l => l.JournalEntry.Date <= asOf)
            .GroupBy(l => new { l.Account.Code, l.Account.Name, l.Account.Type })
            .Select(g => new { g.Key.Code, g.Key.Name, g.Key.Type, Balance = g.Sum(x => x.Debit - x.Credit) })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var debits = rows.Where(r => r.Balance > 0)
            .Select(r => new ReportLine(r.Code, r.Name, "Debit", r.Balance)).ToList();
        var credits = rows.Where(r => r.Balance < 0)
            .Select(r => new ReportLine(r.Code, r.Name, "Credit", -r.Balance)).ToList();

        return new ReportResult("Trial Balance", $"As at {asOf:dd MMM yyyy}",
            new List<ReportSection>
            {
                new("Debits", debits, debits.Sum(l => l.Amount)),
                new("Credits", credits, credits.Sum(l => l.Amount))
            },
            debits.Sum(l => l.Amount) - credits.Sum(l => l.Amount)); // 0 when it balances
    }

    public async Task<ReportResult> ProfitAndLossAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var rows = await PostedLines(businessId)
            .Where(l => l.JournalEntry.Date >= from && l.JournalEntry.Date <= to
                        && (l.Account.Type == AccountType.Income || l.Account.Type == AccountType.Expense))
            .GroupBy(l => new { l.Account.Code, l.Account.Name, l.Account.Type, l.Account.SubType })
            .Select(g => new
            {
                g.Key.Code, g.Key.Name, g.Key.Type, g.Key.SubType,
                Amount = g.Key.Type == AccountType.Income
                    ? g.Sum(x => x.Credit - x.Debit)
                    : g.Sum(x => x.Debit - x.Credit)
            })
            .Where(x => x.Amount != 0)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var income = rows.Where(r => r.Type == AccountType.Income)
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType ?? "Income", r.Amount)).ToList();
        var cos = rows.Where(r => r.Type == AccountType.Expense && r.SubType == "Cost of Sales")
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType!, r.Amount)).ToList();
        var overheads = rows.Where(r => r.Type == AccountType.Expense && r.SubType != "Cost of Sales")
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType ?? "Overheads", r.Amount)).ToList();

        var grossProfit = income.Sum(l => l.Amount) - cos.Sum(l => l.Amount);
        var netProfit = grossProfit - overheads.Sum(l => l.Amount);

        return new ReportResult("Statement of Profit or Loss",
            $"{from:dd MMM yyyy} to {to:dd MMM yyyy}",
            new List<ReportSection>
            {
                new("Income", income, income.Sum(l => l.Amount)),
                new("Cost of Sales", cos, cos.Sum(l => l.Amount)),
                new("Gross Profit", new List<ReportLine>(), grossProfit),
                new("Overheads", overheads, overheads.Sum(l => l.Amount))
            },
            netProfit);
    }

    public async Task<ReportResult> BalanceSheetAsync(Guid businessId, DateOnly asOf)
    {
        var rows = await PostedLines(businessId)
            .Where(l => l.JournalEntry.Date <= asOf)
            .GroupBy(l => new { l.Account.Code, l.Account.Name, l.Account.Type, l.Account.SubType })
            .Select(g => new
            {
                g.Key.Code, g.Key.Name, g.Key.Type, g.Key.SubType,
                Dr = g.Sum(x => x.Debit - x.Credit)
            })
            .ToListAsync();

        var assets = rows.Where(r => r.Type == AccountType.Asset && r.Dr != 0)
            .OrderBy(r => r.Code)
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType ?? "Assets", r.Dr)).ToList();
        var liabilities = rows.Where(r => r.Type == AccountType.Liability && r.Dr != 0)
            .OrderBy(r => r.Code)
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType ?? "Liabilities", -r.Dr)).ToList();
        var equity = rows.Where(r => r.Type == AccountType.Equity && r.Dr != 0)
            .OrderBy(r => r.Code)
            .Select(r => new ReportLine(r.Code, r.Name, r.SubType ?? "Equity", -r.Dr)).ToList();

        // Profit for all periods to date rolls into equity as retained earnings.
        var retained = rows.Where(r => r.Type == AccountType.Income).Sum(r => -r.Dr)
                     - rows.Where(r => r.Type == AccountType.Expense).Sum(r => r.Dr);
        equity.Add(new ReportLine("—", "Profit and Loss Account (cumulative)", "Capital & Reserves", retained));

        var totalAssets = assets.Sum(l => l.Amount);
        var totalLiabEq = liabilities.Sum(l => l.Amount) + equity.Sum(l => l.Amount);

        return new ReportResult("Statement of Financial Position", $"As at {asOf:dd MMM yyyy}",
            new List<ReportSection>
            {
                new("Assets", assets, totalAssets),
                new("Liabilities", liabilities, liabilities.Sum(l => l.Amount)),
                new("Equity", equity, equity.Sum(l => l.Amount))
            },
            totalAssets - totalLiabEq); // 0 when the position balances
    }

    /// <summary>
    /// Direct-method cash summary: every posted movement on bank-flagged accounts,
    /// grouped by the source of the journal (receipts, payments, manual, etc.).
    /// </summary>
    public async Task<ReportResult> CashFlowAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var rows = await PostedLines(businessId)
            .Where(l => l.Account.IsBank && l.JournalEntry.Date >= from && l.JournalEntry.Date <= to)
            .GroupBy(l => l.JournalEntry.Source)
            .Select(g => new { Source = g.Key, Net = g.Sum(x => x.Debit - x.Credit) })
            .ToListAsync();

        var inflows = rows.Where(r => r.Net > 0)
            .Select(r => new ReportLine("", Describe(r.Source), "Inflows", r.Net)).ToList();
        var outflows = rows.Where(r => r.Net < 0)
            .Select(r => new ReportLine("", Describe(r.Source), "Outflows", -r.Net)).ToList();

        var opening = await PostedLines(businessId)
            .Where(l => l.Account.IsBank && l.JournalEntry.Date < from)
            .SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;
        var movement = inflows.Sum(l => l.Amount) - outflows.Sum(l => l.Amount);

        return new ReportResult("Cash Flow Summary", $"{from:dd MMM yyyy} to {to:dd MMM yyyy}",
            new List<ReportSection>
            {
                new("Opening cash", new List<ReportLine>(), opening),
                new("Cash inflows", inflows, inflows.Sum(l => l.Amount)),
                new("Cash outflows", outflows, outflows.Sum(l => l.Amount)),
                new("Closing cash", new List<ReportLine>(), opening + movement)
            },
            movement);
    }

    private static string Describe(SourceType s) => s switch
    {
        SourceType.Receipt => "Customer receipts & money in",
        SourceType.Payment => "Supplier payments & money out",
        SourceType.Manual => "Manual journals",
        SourceType.Reversal => "Reversals",
        _ => s.ToString()
    };
}
