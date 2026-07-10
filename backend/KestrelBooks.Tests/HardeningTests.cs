using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The concurrency guards: unique posted journal numbers, and one journal
/// per source document (the server-side double-post protection).
/// </summary>
public class HardeningTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _bank, _sales;
    private readonly Guid _user = Guid.NewGuid();

    public HardeningTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, bank, _) = TestDb.SeedBusiness(ctx, "Race Ltd");
        _businessId = b.Id; _bank = bank.Id; _sales = sales.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    [Fact]
    public async Task DuplicatePostedNumber_IsRejectedByTheDatabase()
    {
        using var ctx = _db.Create();
        JournalEntry Make(int number) => new()
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Date = new DateOnly(2026, 1, 5),
            Reference = "R", Narrative = "n", Status = JournalStatus.Posted, Number = number,
            Lines =
            {
                new JournalLine { Id = Guid.NewGuid(), AccountId = _bank, Debit = 10, Credit = 0 },
                new JournalLine { Id = Guid.NewGuid(), AccountId = _sales, Debit = 0, Credit = 10 },
            }
        };
        ctx.Journals.Add(Make(1));
        await ctx.SaveChangesAsync();
        ctx.Journals.Add(Make(1)); // same business, same posted number
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Drafts_AllShareNumberZero_WithoutConflict()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        // Two drafts both sit at Number = 0 — the unique index must ignore them.
        for (var i = 0; i < 2; i++)
            await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 5), $"D{i}", "draft",
                SourceType.Manual, null,
                new[] { new DraftLine(_bank, 5, 0, null), new DraftLine(_sales, 0, 5, null) });
        Assert.Equal(2, await ctx.Journals.CountAsync(j => j.Status == JournalStatus.Draft));
    }

    [Fact]
    public async Task SecondJournalForSameSourceDocument_IsRejected()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        var invoiceId = Guid.NewGuid();

        var first = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 6), "INV-1", "inv",
            SourceType.SalesInvoice, invoiceId,
            new[] { new DraftLine(_bank, 50, 0, null), new DraftLine(_sales, 0, 50, null) });
        await svc.PostAsync(_businessId, first.Id, _user);

        // A concurrent double-post would try to create a second journal for the
        // same invoice — the (BusinessId, Source, SourceId) unique index refuses.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 6), "INV-1", "inv again",
                SourceType.SalesInvoice, invoiceId,
                new[] { new DraftLine(_bank, 50, 0, null), new DraftLine(_sales, 0, 50, null) }));
    }

    [Fact]
    public async Task ManualJournals_AreNotSourceConstrained()
    {
        using var ctx = _db.Create();
        var svc = new PostingService(ctx);
        // Manual journals (Source = Manual, SourceId = null) are outside the filter:
        // any number of them may exist.
        for (var i = 0; i < 3; i++)
        {
            var j = await svc.CreateDraftAsync(_businessId, _user, new DateOnly(2026, 1, 7), $"M{i}", "adj",
                SourceType.Manual, null,
                new[] { new DraftLine(_bank, 1, 0, null), new DraftLine(_sales, 0, 1, null) });
            await svc.PostAsync(_businessId, j.Id, _user);
        }
        Assert.Equal(3, await ctx.Journals.CountAsync(j => j.Status == JournalStatus.Posted));
    }

    public void Dispose() => _db.Dispose();
}
