using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// Disposal must remove exactly what the balance sheet holds and put the
/// difference in the P&L — profit when proceeds beat net book value, loss when
/// they don't, and a full write-off of remaining NBV when something is scrapped.
/// </summary>
public class AssetDisposalTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _cost, _accum, _expense, _bank, _disposalPl;

    public AssetDisposalTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, bank, _) = TestDb.SeedBusiness(ctx, "Disposal Ltd");
        _businessId = b.Id;
        _cost = ctx.Accounts.First(a => a.Code == "0020").Id;
        _accum = ctx.Accounts.First(a => a.Code == "0021").Id;
        _expense = ctx.Accounts.First(a => a.Code == "8000").Id;
        _bank = bank.Id;
        _disposalPl = ctx.Accounts.First(a => a.Type == AccountType.Income).Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private DepreciationService Service(Api.Data.AppDbContext ctx) =>
        new(ctx, new PostingService(ctx));

    private Guid SeedAsset(Api.Data.AppDbContext ctx, decimal cost, decimal accumulated,
        AssetStatus status = AssetStatus.InUse, DateOnly? depreciatedThrough = null)
    {
        var asset = new FixedAsset
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Code = "VAN1", Description = "Delivery van",
            Status = status, AcquisitionDate = new DateOnly(2024, 1, 1),
            Cost = cost, ResidualValue = 0, Method = DepreciationMethod.StraightLine,
            UsefulLifeMonths = 60, DepreciationStart = new DateOnly(2024, 1, 1),
            AccumulatedDepreciation = accumulated, DepreciatedThrough = depreciatedThrough,
            CostAccountId = _cost, AccumDepAccountId = _accum, DepExpenseAccountId = _expense,
        };
        ctx.FixedAssets.Add(asset);
        ctx.SaveChanges();
        return asset.Id;
    }

    private static (decimal dr, decimal cr) Sides(JournalEntry j, Guid accountId)
    {
        var lines = j.Lines.Where(l => l.AccountId == accountId).ToList();
        return (lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task SaleAboveNbv_BooksProfit_AndClearsBothAssetAccounts()
    {
        using var ctx = _db.Create();
        // Cost 12,000, depreciated 8,000 → NBV 4,000. Sold for 5,000 → profit 1,000.
        var id = SeedAsset(ctx, 12_000m, 8_000m, depreciatedThrough: new DateOnly(2026, 6, 30));

        var (journal, gainLoss, behind) = await Service(ctx).DisposeAsync(_businessId, id,
            new DateOnly(2026, 6, 30), 5_000m, _bank, _disposalPl, _user);

        Assert.Equal(1_000m, gainLoss);
        Assert.False(behind);

        var posted = await ctx.Journals.Include(j => j.Lines)
            .FirstAsync(j => j.Id == journal.Id);
        Assert.Equal(JournalStatus.Posted, posted.Status);
        Assert.Equal(SourceType.AssetDisposal, posted.Source);

        Assert.Equal((8_000m, 0m), Sides(posted, _accum));      // accumulated removed
        Assert.Equal((0m, 12_000m), Sides(posted, _cost));      // cost removed
        Assert.Equal((5_000m, 0m), Sides(posted, _bank));       // proceeds in
        Assert.Equal((0m, 1_000m), Sides(posted, _disposalPl)); // profit credited

        Assert.Equal(posted.Lines.Sum(l => l.Debit), posted.Lines.Sum(l => l.Credit));

        var asset = await ctx.FixedAssets.FirstAsync(a => a.Id == id);
        Assert.Equal(AssetStatus.Disposed, asset.Status);
        Assert.Equal(5_000m, asset.DisposalProceeds);
        Assert.Equal(1_000m, asset.DisposalGainLoss);
    }

    [Fact]
    public async Task SaleBelowNbv_BooksLoss()
    {
        using var ctx = _db.Create();
        // NBV 4,000, sold for 2,500 → loss 1,500 (debit to the P&L account).
        var id = SeedAsset(ctx, 12_000m, 8_000m, depreciatedThrough: new DateOnly(2026, 6, 30));

        var (journal, gainLoss, _) = await Service(ctx).DisposeAsync(_businessId, id,
            new DateOnly(2026, 6, 30), 2_500m, _bank, _disposalPl, _user);

        Assert.Equal(-1_500m, gainLoss);
        var posted = await ctx.Journals.Include(j => j.Lines).FirstAsync(j => j.Id == journal.Id);
        Assert.Equal((1_500m, 0m), Sides(posted, _disposalPl));
        Assert.Equal(posted.Lines.Sum(l => l.Debit), posted.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Scrapping_WithNoProceeds_WritesOffRemainingNbv()
    {
        using var ctx = _db.Create();
        var id = SeedAsset(ctx, 12_000m, 8_000m, depreciatedThrough: new DateOnly(2026, 6, 30));

        var (journal, gainLoss, _) = await Service(ctx).DisposeAsync(_businessId, id,
            new DateOnly(2026, 6, 30), 0m, _bank, _disposalPl, _user);

        Assert.Equal(-4_000m, gainLoss);                        // the whole NBV is the loss
        var posted = await ctx.Journals.Include(j => j.Lines).FirstAsync(j => j.Id == journal.Id);
        Assert.DoesNotContain(posted.Lines, l => l.AccountId == _bank);   // no proceeds leg
        Assert.Equal(posted.Lines.Sum(l => l.Debit), posted.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task FullyDepreciatedAsset_SoldForScrapValue_IsAllProfit()
    {
        using var ctx = _db.Create();
        var id = SeedAsset(ctx, 12_000m, 12_000m, depreciatedThrough: new DateOnly(2026, 6, 30));

        var (_, gainLoss, _) = await Service(ctx).DisposeAsync(_businessId, id,
            new DateOnly(2026, 6, 30), 300m, _bank, _disposalPl, _user);

        Assert.Equal(300m, gainLoss);
    }

    [Fact]
    public async Task Guards_BlockDoubleDisposal_NegativeProceeds_AndPreAcquisitionDates()
    {
        using var ctx = _db.Create();
        var id = SeedAsset(ctx, 1_000m, 0m, depreciatedThrough: new DateOnly(2026, 6, 30));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(ctx).DisposeAsync(
            _businessId, id, new DateOnly(2020, 1, 1), 0m, _bank, _disposalPl, _user));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(ctx).DisposeAsync(
            _businessId, id, new DateOnly(2026, 6, 30), -5m, _bank, _disposalPl, _user));

        await Service(ctx).DisposeAsync(_businessId, id, new DateOnly(2026, 6, 30), 100m,
            _bank, _disposalPl, _user);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(ctx).DisposeAsync(
            _businessId, id, new DateOnly(2026, 6, 30), 100m, _bank, _disposalPl, _user));
    }

    [Fact]
    public async Task DisposalBeforeDepreciationCaughtUp_IsFlagged()
    {
        using var ctx = _db.Create();
        // Depreciation only posted to March; disposing in June.
        var id = SeedAsset(ctx, 12_000m, 5_000m, depreciatedThrough: new DateOnly(2026, 3, 31));

        var (_, _, behind) = await Service(ctx).DisposeAsync(_businessId, id,
            new DateOnly(2026, 6, 15), 6_000m, _bank, _disposalPl, _user);

        Assert.True(behind);
    }

    public void Dispose() => _db.Dispose();
}
