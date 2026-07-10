using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>The double-entry engine's contract: balance, sequence, immutability, reversal symmetry.</summary>
public class PostingServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _bank, _sales;
    private readonly Guid _user = Guid.NewGuid();

    public PostingServiceTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, bank, _) = TestDb.SeedBusiness(ctx, "Test Ltd");
        _businessId = b.Id; _bank = bank.Id; _sales = sales.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private static DraftLine Dr(Guid acc, decimal amt) => new(acc, amt, 0, null);
    private static DraftLine Cr(Guid acc, decimal amt) => new(acc, 0, amt, null);

    [Fact]
    public async Task BalancedDraft_Creates()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        var j = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 10), "T1", "cash sale",
            SourceType.Manual, null, new[] { Dr(_bank, 100), Cr(_sales, 100) });
        Assert.Equal(JournalStatus.Draft, j.Status);
        Assert.Equal(0, j.Number); // numbers are assigned at posting, not creation
    }

    [Theory]
    [InlineData(100, 99)]   // unbalanced
    [InlineData(0, 0)]      // zero-value
    public async Task InvalidJournals_AreRejected(decimal dr, decimal cr)
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 10), "T", "bad",
                SourceType.Manual, null, new[] { Dr(_bank, dr), Cr(_sales, cr) }));
    }

    [Fact]
    public async Task TwoSidedLine_IsRejected()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 10), "T", "bad",
                SourceType.Manual, null,
                new[] { new DraftLine(_bank, 50, 50, null), Cr(_sales, 0) }));
    }

    [Fact]
    public async Task Posting_AssignsSequentialNumbers_AndFreezes()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        var j1 = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 10), "T1", "one",
            SourceType.Manual, null, new[] { Dr(_bank, 10), Cr(_sales, 10) });
        var j2 = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 11), "T2", "two",
            SourceType.Manual, null, new[] { Dr(_bank, 20), Cr(_sales, 20) });
        await svc.PostAsync(_businessId, j1.Id, _user);
        await svc.PostAsync(_businessId, j2.Id, _user);
        Assert.Equal(1, j1.Number);
        Assert.Equal(2, j2.Number);
        // Posting the same journal twice must fail — posted entries are frozen.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(_businessId, j1.Id, _user));
    }

    [Fact]
    public async Task Reversal_IsAMirrorImage_AndLinksBothWays()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        var j = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 10), "T1", "original",
            SourceType.Manual, null, new[] { Dr(_bank, 75.50m), Cr(_sales, 75.50m) });
        await svc.PostAsync(_businessId, j.Id, _user);

        var rev = await svc.ReverseAsync(_businessId, j.Id, _user, new DateOnly(2026, 1, 31));

        Assert.Equal(JournalStatus.Posted, rev.Status);
        Assert.Equal(j.Id, rev.ReversalOfId);
        Assert.Equal(JournalStatus.Reversed,
            (await ctx.Journals.FirstAsync(x => x.Id == j.Id)).Status);
        // Mirror image: the bank line that was a debit is now a credit of the same amount.
        var bankLine = rev.Lines.First(l => l.AccountId == _bank);
        Assert.Equal(0, bankLine.Debit);
        Assert.Equal(75.50m, bankLine.Credit);
        // A reversed journal cannot be reversed again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReverseAsync(_businessId, j.Id, _user, null));
    }

    public void Dispose() => _db.Dispose();
}
