using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The sign convention is the whole game here. A £500 underspend and a £500
/// overspend are the same number with opposite meanings, and a variance report
/// that doesn't say which is worse than no report at all.
/// </summary>
public class BudgetTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _sales, _expense, _bank, _budgetId;

    public BudgetTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, bank, _) = TestDb.SeedBusiness(ctx, "Budgeted Ltd");
        _businessId = b.Id; _sales = sales.Id; _bank = bank.Id;
        _expense = ctx.Accounts.First(a => a.Type == AccountType.Expense).Id;

        var budget = new Budget
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, Name = "2026 plan",
            StartMonth = new DateOnly(2026, 1, 1), Months = 12,
        };
        ctx.Budgets.Add(budget);
        ctx.SaveChanges();
        _budgetId = budget.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private void Budgeted(Api.Data.AppDbContext ctx, Guid accountId, DateOnly month, decimal amount)
    {
        ctx.BudgetLines.Add(new BudgetLine
        {
            Id = Guid.NewGuid(), BudgetId = _budgetId, BusinessId = _businessId,
            AccountId = accountId, Month = month, Amount = amount,
        });
        ctx.SaveChanges();
    }

    private async Task Actual(Api.Data.AppDbContext ctx, DateOnly date, Guid accountId,
        decimal amount, bool isIncome)
    {
        var posting = new PostingService(ctx);
        var lines = isIncome
            ? new[] { new DraftLine(_bank, amount, 0, "cash"), new DraftLine(accountId, 0, amount, "income") }
            : new[] { new DraftLine(accountId, amount, 0, "cost"), new DraftLine(_bank, 0, amount, "cash") };
        var j = await posting.CreateDraftAsync(_businessId, _user, date, "R", "actual",
            SourceType.Manual, null, lines);
        await posting.PostAsync(_businessId, j.Id, _user);
    }

    [Fact]
    public async Task MoreIncomeThanBudgeted_IsFavourable_LessCostIsToo()
    {
        using var ctx = _db.Create();
        Budgeted(ctx, _sales, new DateOnly(2026, 1, 1), 10_000m);
        Budgeted(ctx, _expense, new DateOnly(2026, 1, 1), 4_000m);
        await Actual(ctx, new DateOnly(2026, 1, 15), _sales, 12_000m, isIncome: true);
        await Actual(ctx, new DateOnly(2026, 1, 20), _expense, 3_500m, isIncome: false);

        var report = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);

        var income = report.Income.Single(r => r.Code == "4000");
        Assert.Equal(12_000m, income.Actual);      // shown positive, not credit-negative
        Assert.Equal(2_000m, income.Variance);
        Assert.True(income.Favourable);
        Assert.Equal(20m, income.VariancePercent);

        var cost = report.Expenses.Single();
        Assert.Equal(3_500m, cost.Actual);
        Assert.Equal(-500m, cost.Variance);
        Assert.True(cost.Favourable);              // underspending is good

        Assert.Equal(8_500m, report.ActualProfit); // 12,000 − 3,500
        Assert.Equal(6_000m, report.BudgetProfit); // 10,000 − 4,000
        Assert.Equal(2_500m, report.ProfitVariance);
    }

    [Fact]
    public async Task Overspending_IsUnfavourable_EvenThoughTheNumberLooksTheSame()
    {
        using var ctx = _db.Create();
        Budgeted(ctx, _expense, new DateOnly(2026, 1, 1), 1_000m);
        await Actual(ctx, new DateOnly(2026, 1, 10), _expense, 1_500m, isIncome: false);

        var report = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);

        var cost = report.Expenses.Single();
        Assert.Equal(500m, cost.Variance);
        Assert.False(cost.Favourable);
        Assert.Equal(50m, cost.VariancePercent);
    }

    [Fact]
    public async Task OnlyTheRequestedPeriod_IsCompared()
    {
        using var ctx = _db.Create();
        Budgeted(ctx, _expense, new DateOnly(2026, 1, 1), 1_000m);
        Budgeted(ctx, _expense, new DateOnly(2026, 2, 1), 1_000m);
        await Actual(ctx, new DateOnly(2026, 1, 10), _expense, 900m, isIncome: false);
        await Actual(ctx, new DateOnly(2026, 2, 10), _expense, 1_100m, isIncome: false);

        var january = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);
        Assert.Equal(900m, january.Expenses.Single().Actual);
        Assert.Equal(1_000m, january.Expenses.Single().Budget);

        var yearToDate = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), null);
        Assert.Equal(2_000m, yearToDate.Expenses.Single().Actual);
        Assert.Equal(2_000m, yearToDate.Expenses.Single().Budget);
        Assert.Equal(0m, yearToDate.Expenses.Single().Variance);
    }

    [Fact]
    public async Task AccountsWithNoActivityAndNoBudget_AreOmitted_ButUnbudgetedSpendIsShown()
    {
        using var ctx = _db.Create();
        await Actual(ctx, new DateOnly(2026, 1, 10), _expense, 250m, isIncome: false);

        var report = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);

        var cost = report.Expenses.Single();
        Assert.Equal(250m, cost.Actual);
        Assert.Equal(0m, cost.Budget);
        Assert.False(cost.Favourable);
        Assert.Null(cost.VariancePercent);   // no percentage against a zero budget
        Assert.Empty(report.Income);
    }

    [Fact]
    public async Task SeedingFromActuals_SpreadsAnUpliftedMonthlyAverage()
    {
        using var ctx = _db.Create();
        // 3,000 of cost over three months = 1,000 a month; +10% = 1,100.
        await Actual(ctx, new DateOnly(2025, 1, 15), _expense, 1_000m, isIncome: false);
        await Actual(ctx, new DateOnly(2025, 2, 15), _expense, 1_000m, isIncome: false);
        await Actual(ctx, new DateOnly(2025, 3, 15), _expense, 1_000m, isIncome: false);

        var created = await new BudgetService(ctx).SeedFromActualsAsync(_businessId, _budgetId,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), 10m);

        Assert.Equal(12, created);   // one line per month of the budget
        var report = await new BudgetService(ctx).VarianceAsync(_businessId, _budgetId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);
        Assert.Equal(1_100m, report.Expenses.Single().Budget);
    }

    public void Dispose() => _db.Dispose();
}
