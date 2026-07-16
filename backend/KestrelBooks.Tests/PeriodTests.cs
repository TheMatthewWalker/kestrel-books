using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class PeriodTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _bank, _sales, _expense;

    public PeriodTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, bank, _) = TestDb.SeedBusiness(ctx, "Period Ltd");
        _businessId = b.Id; _bank = bank.Id; _sales = sales.Id;
        _expense = ctx.Accounts.First(a => a.Code == "7000").Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private static DraftLine Dr(Guid acc, decimal amt) => new(acc, amt, 0, null);
    private static DraftLine Cr(Guid acc, decimal amt) => new(acc, 0, amt, null);

    private async Task<JournalEntry> PostSimple(PostingService svc, DateOnly date, Guid dr, Guid cr, decimal amount)
    {
        var j = await svc.CreateDraftAsync(_businessId, _user, date, "T", "t",
            SourceType.Manual, null, new[] { Dr(dr, amount), Cr(cr, amount) });
        await svc.PostAsync(_businessId, j.Id, _user);
        return j;
    }

    [Fact]
    public async Task LockedPeriod_BlocksCreatePostAndReverse()
    {
        using var ctx = _db.Create();
        var posting = new PostingService(ctx);
        var periods = new PeriodService(ctx, posting);

        // Draft created before the lock, and a posted journal inside the future lock window.
        var preLockDraft = await posting.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 3, 10), "D", "d",
            SourceType.Manual, null, new[] { Dr(_bank, 10), Cr(_sales, 10) });
        var postedInside = await PostSimple(posting, new DateOnly(2026, 3, 15), _bank, _sales, 20);

        await periods.SetLockAsync(_businessId, new DateOnly(2026, 3, 31));

        // 1. Creating a journal dated inside the locked window fails.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            posting.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 3, 20), "X", "x",
                SourceType.Manual, null, new[] { Dr(_bank, 5), Cr(_sales, 5) }));
        // 2. Posting the pre-existing draft (dated inside the window) fails.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            posting.PostAsync(_businessId, preLockDraft.Id, _user));
        // 3. Reversing a posted journal inside the window fails.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            posting.ReverseAsync(_businessId, postedInside.Id, _user, new DateOnly(2026, 4, 5)));
        // 4. Posting after the lock date works normally.
        var after = await PostSimple(posting, new DateOnly(2026, 4, 1), _bank, _sales, 30);
        Assert.Equal(JournalStatus.Posted, after.Status);
        // 5. Unlocking restores the past.
        await periods.SetLockAsync(_businessId, null);
        await posting.PostAsync(_businessId, preLockDraft.Id, _user);
        Assert.Equal(JournalStatus.Posted, preLockDraft.Status);
    }

    [Fact]
    public async Task YearEndClose_ZeroesPandL_IntoRetainedEarnings_AndLocks()
    {
        using var ctx = _db.Create();
        var posting = new PostingService(ctx);
        var periods = new PeriodService(ctx, posting);
        var yearEnd = new DateOnly(2026, 3, 31);

        // Year's trading: income 100 (Cr sales), expense 40 (Dr overheads).
        await PostSimple(posting, new DateOnly(2025, 6, 1), _bank, _sales, 100);
        await PostSimple(posting, new DateOnly(2025, 9, 1), _expense, _bank, 40);

        var close = await periods.CloseYearAsync(_businessId, yearEnd, _user);

        // Golden figures: Dr Sales 100, Cr Overheads 40, Cr Retained Earnings 60.
        var retained = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.RetainedEarnings);
        Assert.Equal(100m, close.Lines.First(l => l.AccountId == _sales).Debit);
        Assert.Equal(40m, close.Lines.First(l => l.AccountId == _expense).Credit);
        Assert.Equal(60m, close.Lines.First(l => l.AccountId == retained.Id).Credit);

        // Post-close cumulative balances: P&L accounts zero, RE holds the profit.
        var salesBal = await ctx.JournalLines.Where(l => l.AccountId == _sales).ToListAsync();
        Assert.Equal(0m, salesBal.Sum(l => l.Credit - l.Debit));

        // Year is locked automatically.
        Assert.Equal(yearEnd, (await ctx.Businesses.FirstAsync(b => b.Id == _businessId)).LockedThrough);
        // Second close of the same year end is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => periods.CloseYearAsync(_businessId, yearEnd, _user));
    }

    [Fact]
    public async Task PandLReport_IgnoresTheCloseJournal()
    {
        var (factory, tenant) = TestDb.InMemory($"pl-{Guid.NewGuid()}");
        using var ctx = factory();
        var (b, _, sales, bank, _) = TestDb.SeedBusiness(ctx, "PL Ltd");
        var expense = ctx.Accounts.First(a => a.Code == "7000");
        tenant.Set(b.Id, BusinessRole.Owner);
        var posting = new PostingService(ctx);
        var periods = new PeriodService(ctx, posting);

        async Task Post(DateOnly date, Guid dr, Guid cr, decimal amt)
        {
            var j = await posting.CreateDraftAsync(b.Id, _user, date, "T", "t",
                SourceType.Manual, null, new[] { Dr(dr, amt), Cr(cr, amt) });
            await posting.PostAsync(b.Id, j.Id, _user);
        }
        await Post(new DateOnly(2025, 6, 1), bank.Id, sales.Id, 100);
        await Post(new DateOnly(2025, 9, 1), expense.Id, bank.Id, 40);
        await periods.CloseYearAsync(b.Id, new DateOnly(2026, 3, 31), _user);

        // The closed year's P&L must still report the £60 profit, not zero.
        var report = await new ReportService(ctx).ProfitAndLossAsync(
            b.Id, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31));
        Assert.Equal(60m, report.Total);

        // A second close attempt at a later date finds nothing left to close.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            periods.CloseYearAsync(b.Id, new DateOnly(2027, 3, 31), _user));
    }

    public void Dispose() => _db.Dispose();
}
