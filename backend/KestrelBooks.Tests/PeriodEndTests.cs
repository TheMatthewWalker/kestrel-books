using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The month-end adjustments. The two behaviours that matter: an accrual must
/// reverse itself in the following period (or the cost is counted twice when the
/// invoice arrives), and a prepayment must release in equal instalments that add
/// back to exactly the original amount despite rounding.
/// </summary>
public class PeriodEndTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _expense, _income, _balanceSheet;

    public PeriodEndTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, sales, _, _) = TestDb.SeedBusiness(ctx, "Month End Ltd");
        _businessId = b.Id;
        _expense = ctx.Accounts.First(a => a.Type == AccountType.Expense).Id;
        _income = sales.Id;
        _balanceSheet = debtors.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private PeriodEndService Service(Api.Data.AppDbContext ctx) =>
        new(ctx, new PostingService(ctx), _db.Tenant);

    private static decimal Net(JournalEntry j, Guid accountId) =>
        j.Lines.Where(l => l.AccountId == accountId).Sum(l => l.Debit - l.Credit);

    [Fact]
    public async Task Accrual_PostsTheCost_ThenReversesOnTheFirstOfNextMonth()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Accrual,
            "Electricity for June, invoice not yet received", 450m,
            _expense, _balanceSheet, new DateOnly(2026, 6, 30), 1);

        var created = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 6, 30), _user);

        Assert.Equal(2, created.Count); // the accrual and its reversal
        var journals = await ctx.Journals.Include(j => j.Lines)
            .Where(j => created.Contains(j.Id)).OrderBy(j => j.Date).ToListAsync();

        // June: the cost lands in the period it belongs to.
        Assert.Equal(new DateOnly(2026, 6, 30), journals[0].Date);
        Assert.Equal(450m, Net(journals[0], _expense));
        Assert.Equal(-450m, Net(journals[0], _balanceSheet));

        // July: reversed, so the real invoice doesn't double count.
        Assert.Equal(new DateOnly(2026, 7, 1), journals[1].Date);
        Assert.Equal(-450m, Net(journals[1], _expense));
        Assert.Equal(450m, Net(journals[1], _balanceSheet));

        // Net effect across both periods is nil — the accrual only moves timing.
        Assert.Equal(0m, journals.Sum(j => Net(j, _expense)));

        var reloaded = await ctx.PeriodEndSchedules.FirstAsync(x => x.Id == s.Id);
        Assert.Equal(ScheduleStatus.Completed, reloaded.Status);
    }

    [Fact]
    public async Task Accrual_DoesNotPostTwice_OnASecondRun()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Accrual, "Audit fee", 1_200m,
            _expense, _balanceSheet, new DateOnly(2026, 6, 30), 1);

        await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 6, 30), _user);
        var second = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 12, 31), _user);

        Assert.Empty(second);
    }

    [Fact]
    public async Task Prepayment_ReleasesMonthly_AndTheInstalmentsSumToTheWholeAmount()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        // 1,000 over 3 months = 333.33, 333.33, then 333.34 to clear the rounding.
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Prepayment,
            "Insurance paid annually", 1_000m, _expense, _balanceSheet,
            new DateOnly(2026, 1, 31), 3);

        var created = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 3, 31), _user);

        Assert.Equal(3, created.Count);
        var journals = await ctx.Journals.Include(j => j.Lines)
            .Where(j => created.Contains(j.Id)).OrderBy(j => j.Date).ToListAsync();

        Assert.Equal(333.33m, Net(journals[0], _expense));
        Assert.Equal(333.33m, Net(journals[1], _expense));
        Assert.Equal(333.34m, Net(journals[2], _expense));   // final instalment takes the remainder
        Assert.Equal(1_000m, journals.Sum(j => Net(j, _expense)));
        Assert.Equal(-1_000m, journals.Sum(j => Net(j, _balanceSheet)));

        var reloaded = await ctx.PeriodEndSchedules.FirstAsync(x => x.Id == s.Id);
        Assert.Equal(ScheduleStatus.Completed, reloaded.Status);
        Assert.Equal(3, reloaded.PeriodsReleased);
    }

    [Fact]
    public async Task Prepayment_OnlyReleasesWhatIsDue()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Prepayment, "Rent in advance",
            1_200m, _expense, _balanceSheet, new DateOnly(2026, 1, 31), 12);

        // Only two months have passed.
        var created = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 2, 28), _user);

        Assert.Equal(2, created.Count);
        var reloaded = await ctx.PeriodEndSchedules.FirstAsync(x => x.Id == s.Id);
        Assert.Equal(ScheduleStatus.Active, reloaded.Status);
        Assert.Equal(2, reloaded.PeriodsReleased);
        Assert.Equal(new DateOnly(2026, 3, 31), reloaded.NextRunDate);
    }

    [Fact]
    public async Task DeferredIncome_ReleasesIntoIncome_TheOppositeWayRound()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.DeferredIncome,
            "Annual support contract billed up front", 600m, _income, _balanceSheet,
            new DateOnly(2026, 1, 31), 2);

        var created = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 2, 28), _user);
        var journals = await ctx.Journals.Include(j => j.Lines)
            .Where(j => created.Contains(j.Id)).ToListAsync();

        // Income is credited as it is earned; the balance sheet liability is debited away.
        Assert.Equal(-600m, journals.Sum(j => Net(j, _income)));
        Assert.Equal(600m, journals.Sum(j => Net(j, _balanceSheet)));
    }

    [Fact]
    public async Task Cancel_StopsFurtherReleases()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Prepayment, "Software licence",
            1_200m, _expense, _balanceSheet, new DateOnly(2026, 1, 31), 12);
        await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 1, 31), _user);

        await svc.CancelAsync(_businessId, s.Id);
        var after = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 12, 31), _user);

        Assert.Empty(after);
    }

    [Fact]
    public async Task Guards_RejectNonPositiveAmounts_AndZeroPeriodSpreads()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(
            _businessId, _user, PeriodEndKind.Accrual, "Bad", 0m, _expense, _balanceSheet,
            new DateOnly(2026, 6, 30), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(
            _businessId, _user, PeriodEndKind.Prepayment, "Bad", 100m, _expense, _balanceSheet,
            new DateOnly(2026, 6, 30), 0));
    }

    [Fact]
    public async Task AMonthEndSchedule_StaysOnTheMonthEnd_ThroughFebruary()
    {
        using var ctx = _db.Create();
        var svc = Service(ctx);
        // Starting 31 January: February can only reach the 28th, but March must
        // return to the 31st rather than staying stuck on the 28th for good.
        var s = await svc.CreateAsync(_businessId, _user, PeriodEndKind.Prepayment,
            "Annual cover paid 31 January", 1_200m, _expense, _balanceSheet,
            new DateOnly(2026, 1, 31), 12);

        var created = await svc.RunAsync(_businessId, s.Id, new DateOnly(2026, 4, 30), _user);

        var dates = await ctx.Journals
            .Where(j => created.Contains(j.Id))
            .OrderBy(j => j.Date)
            .Select(j => j.Date)
            .ToListAsync();

        Assert.Equal(new[]
        {
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31),
            new DateOnly(2026, 4, 30),
        }, dates);
    }

    public void Dispose() => _db.Dispose();
}
