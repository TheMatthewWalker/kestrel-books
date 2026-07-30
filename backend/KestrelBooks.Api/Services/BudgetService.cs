using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record VarianceRow(string Code, string Name, AccountType Type,
    decimal Actual, decimal Budget, decimal Variance, decimal? VariancePercent, bool Favourable);

public record VarianceReport(string BudgetName, DateOnly From, DateOnly To,
    List<VarianceRow> Income, List<VarianceRow> Expenses,
    decimal ActualProfit, decimal BudgetProfit, decimal ProfitVariance);

public class BudgetService
{
    private readonly AppDbContext _db;
    public BudgetService(AppDbContext db) => _db = db;

    /// <summary>
    /// Budget against actual for a period.
    ///
    /// The sign convention is the one a manager expects rather than the one the
    /// ledger uses: income and expenses are both shown positive, and "favourable"
    /// means what it should — more income than budgeted, or less cost. A £500
    /// underspend and a £500 overspend are the same number with opposite
    /// meanings, and a report that does not say which is nearly useless.
    /// </summary>
    public async Task<VarianceReport> VarianceAsync(Guid businessId, Guid budgetId,
        DateOnly from, DateOnly to, Guid? trackingOptionId)
    {
        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId && b.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Budget not found.");

        var accounts = await _db.Accounts
            .Where(a => a.BusinessId == businessId
                        && (a.Type == AccountType.Income || a.Type == AccountType.Expense))
            .Select(a => new { a.Id, a.Code, a.Name, a.Type })
            .ToListAsync();

        // Actuals: posted journal lines in the window, aggregated client-side
        // because SQLite cannot translate decimal sums server-side.
        var actualLines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date >= from && l.JournalEntry.Date <= to
                        && l.JournalEntry.Source != SourceType.YearEndClose
                        && (trackingOptionId == null || l.TrackingOptionId == trackingOptionId))
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync();

        var actualByAccount = actualLines
            .GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));

        var budgetLines = await _db.BudgetLines
            .Where(l => l.BudgetId == budgetId && l.BusinessId == businessId
                        && l.Month >= new DateOnly(from.Year, from.Month, 1)
                        && l.Month <= to
                        && (trackingOptionId == null || l.TrackingOptionId == trackingOptionId))
            .Select(l => new { l.AccountId, l.Amount })
            .ToListAsync();

        var budgetByAccount = budgetLines
            .GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        var income = new List<VarianceRow>();
        var expenses = new List<VarianceRow>();

        foreach (var account in accounts.OrderBy(a => a.Code))
        {
            actualByAccount.TryGetValue(account.Id, out var netDr);
            budgetByAccount.TryGetValue(account.Id, out var budgeted);

            // Income sits credit-side in the ledger, so flip it to read positive.
            var actual = account.Type == AccountType.Income ? -netDr : netDr;
            if (actual == 0 && budgeted == 0) continue;

            var variance = actual - budgeted;
            // More income than budget is good; more cost than budget is not.
            var favourable = account.Type == AccountType.Income ? variance >= 0 : variance <= 0;
            decimal? pct = budgeted == 0 ? null
                : decimal.Round(variance / Math.Abs(budgeted) * 100, 1, MidpointRounding.AwayFromZero);

            var row = new VarianceRow(account.Code, account.Name, account.Type,
                decimal.Round(actual, 2), decimal.Round(budgeted, 2),
                decimal.Round(variance, 2), pct, favourable);

            if (account.Type == AccountType.Income) income.Add(row); else expenses.Add(row);
        }

        var actualProfit = income.Sum(r => r.Actual) - expenses.Sum(r => r.Actual);
        var budgetProfit = income.Sum(r => r.Budget) - expenses.Sum(r => r.Budget);

        return new VarianceReport(budget.Name, from, to, income, expenses,
            decimal.Round(actualProfit, 2), decimal.Round(budgetProfit, 2),
            decimal.Round(actualProfit - budgetProfit, 2));
    }

    /// <summary>
    /// Seeds a budget from what actually happened over a previous period, with an
    /// optional uplift — how most budgets genuinely get built, because last year
    /// plus a bit is a better starting point than a blank grid.
    /// </summary>
    public async Task<int> SeedFromActualsAsync(Guid businessId, Guid budgetId,
        DateOnly sourceFrom, DateOnly sourceTo, decimal upliftPercent)
    {
        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId && b.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Budget not found.");

        var accountTypes = await _db.Accounts
            .Where(a => a.BusinessId == businessId
                        && (a.Type == AccountType.Income || a.Type == AccountType.Expense))
            .ToDictionaryAsync(a => a.Id, a => a.Type);

        var lines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date >= sourceFrom && l.JournalEntry.Date <= sourceTo
                        && l.JournalEntry.Source != SourceType.YearEndClose)
            .Select(l => new { l.AccountId, l.Debit, l.Credit, l.JournalEntry.Date })
            .ToListAsync();

        var monthsInSource = Math.Max(1,
            ((sourceTo.Year - sourceFrom.Year) * 12) + sourceTo.Month - sourceFrom.Month + 1);
        var multiplier = 1 + (upliftPercent / 100m);

        var existing = await _db.BudgetLines.Where(l => l.BudgetId == budgetId).ToListAsync();
        _db.BudgetLines.RemoveRange(existing);

        var created = 0;
        foreach (var group in lines.GroupBy(l => l.AccountId))
        {
            if (!accountTypes.TryGetValue(group.Key, out var type)) continue;
            var netDr = group.Sum(l => l.Debit - l.Credit);
            var total = type == AccountType.Income ? -netDr : netDr;
            if (total == 0) continue;

            var perMonth = decimal.Round(total / monthsInSource * multiplier, 2,
                MidpointRounding.AwayFromZero);
            if (perMonth == 0) continue;

            for (var i = 0; i < budget.Months; i++)
            {
                _db.BudgetLines.Add(new BudgetLine
                {
                    Id = Guid.NewGuid(), BudgetId = budgetId, BusinessId = businessId,
                    AccountId = group.Key, Month = budget.StartMonth.AddMonths(i), Amount = perMonth,
                });
                created++;
            }
        }
        await _db.SaveChangesAsync();
        return created;
    }
}
