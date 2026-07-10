using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>Straight-line and reducing-balance golden figures, residual floor, idempotent runs.</summary>
public class DepreciationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _cost, _accum, _expense;

    public DepreciationTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Asset Ltd");
        _businessId = b.Id;
        _cost = ctx.Accounts.First(a => a.Code == "0020").Id;
        _accum = ctx.Accounts.First(a => a.Code == "0021").Id;
        _expense = ctx.Accounts.First(a => a.Code == "8000").Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private FixedAsset NewAsset(decimal cost, decimal residual, DepreciationMethod method,
        int lifeMonths = 0, decimal ratePct = 0) => new()
    {
        Id = Guid.NewGuid(), BusinessId = _businessId, Code = "A1", Description = "Machine",
        Status = AssetStatus.InUse, AcquisitionDate = new DateOnly(2026, 1, 1),
        Cost = cost, ResidualValue = residual, Method = method,
        UsefulLifeMonths = lifeMonths, AnnualRatePercent = ratePct,
        DepreciationStart = new DateOnly(2026, 1, 1),
        CostAccountId = _cost, AccumDepAccountId = _accum, DepExpenseAccountId = _expense,
    };

    [Fact]
    public void StraightLine_Charge_IsCostLessResidualOverLife()
    {
        // (1200 − 200) / 20 months = £50/month
        var a = NewAsset(1200, 200, DepreciationMethod.StraightLine, lifeMonths: 20);
        Assert.Equal(50m, DepreciationService.MonthlyCharge(a));
    }

    [Fact]
    public void ReducingBalance_Charge_IsNbvTimesMonthlyRate()
    {
        // 1000 × 24% / 12 = £20 in month 1; NBV 980 × 2% = £19.60 in month 2
        var a = NewAsset(1000, 0, DepreciationMethod.ReducingBalance, ratePct: 24);
        Assert.Equal(20m, DepreciationService.MonthlyCharge(a));
        a.AccumulatedDepreciation = 20m;
        Assert.Equal(19.60m, DepreciationService.MonthlyCharge(a));
    }

    [Fact]
    public void Charge_NeverDepreciatesBelowResidual()
    {
        // Depreciable remainder is 1200 − 200 − 980 = £20, so the £50 charge caps at £20.
        var a = NewAsset(1200, 200, DepreciationMethod.StraightLine, lifeMonths: 20);
        a.AccumulatedDepreciation = 980m;
        Assert.Equal(20m, DepreciationService.MonthlyCharge(a));
        a.AccumulatedDepreciation = 1000m; // fully depreciated to residual
        Assert.Equal(0m, DepreciationService.MonthlyCharge(a));
    }

    [Fact]
    public async Task MonthRun_PostsOnce_AndIsIdempotent()
    {
        using var ctx = _db.Create();
        var svc = new DepreciationService(ctx, new PostingService(ctx));
        var asset = NewAsset(1200, 0, DepreciationMethod.StraightLine, lifeMonths: 12); // £100/mo
        ctx.FixedAssets.Add(asset);
        await ctx.SaveChangesAsync();

        var journal = await svc.RunMonthAsync(_businessId, 2026, 1, _user);
        Assert.NotNull(journal);
        Assert.Equal(100m, journal!.Lines.First(l => l.AccountId == _expense).Debit);
        Assert.Equal(100m, journal.Lines.First(l => l.AccountId == _accum).Credit);
        Assert.Equal(100m, asset.AccumulatedDepreciation);
        Assert.Equal(new DateOnly(2026, 1, 31), asset.DepreciatedThrough);

        // Running the same month again does nothing.
        Assert.Null(await svc.RunMonthAsync(_businessId, 2026, 1, _user));
        Assert.Equal(100m, asset.AccumulatedDepreciation);
    }

    [Fact]
    public async Task Capitalisation_MovesAucToCost_AndStartsThePlan()
    {
        using var ctx = _db.Create();
        var svc = new DepreciationService(ctx, new PostingService(ctx));
        var asset = NewAsset(5000, 0, DepreciationMethod.StraightLine, lifeMonths: 50);
        asset.Status = AssetStatus.UnderConstruction;
        ctx.FixedAssets.Add(asset);
        await ctx.SaveChangesAsync();

        var journal = await svc.CapitaliseAsync(_businessId, asset.Id, new DateOnly(2026, 6, 15), _user);

        var auc = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.AssetsUnderConstruction);
        Assert.Equal(5000m, journal.Lines.First(l => l.AccountId == _cost).Debit);
        Assert.Equal(5000m, journal.Lines.First(l => l.AccountId == auc.Id).Credit);
        Assert.Equal(AssetStatus.InUse, asset.Status);
        Assert.Equal(new DateOnly(2026, 6, 15), asset.DepreciationStart);
    }

    public void Dispose() => _db.Dispose();
}
