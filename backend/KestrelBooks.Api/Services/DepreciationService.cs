using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Depreciation automation.
///
/// Straight line:   monthly charge = (cost − residual) / useful life in months.
/// Reducing balance: monthly charge = net book value × (annual rate / 12).
///
/// A run covers one calendar month per business. Each asset is charged at most
/// once per month (tracked via DepreciatedThrough) and never below residual value.
/// The run posts a single journal: Dr depreciation expense / Cr accumulated depreciation
/// per asset.
/// </summary>
public class DepreciationService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public DepreciationService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    public static decimal MonthlyCharge(FixedAsset a)
    {
        var depreciable = a.Cost - a.ResidualValue - a.AccumulatedDepreciation;
        if (depreciable <= 0) return 0;
        decimal charge = a.Method switch
        {
            DepreciationMethod.StraightLine =>
                a.UsefulLifeMonths > 0 ? (a.Cost - a.ResidualValue) / a.UsefulLifeMonths : 0,
            DepreciationMethod.ReducingBalance =>
                a.NetBookValue * (a.AnnualRatePercent / 100m) / 12m,
            _ => 0
        };
        charge = Math.Round(charge, 2, MidpointRounding.AwayFromZero);
        return Math.Min(charge, depreciable); // never depreciate below residual
    }

    public async Task<JournalEntry?> RunMonthAsync(Guid businessId, int year, int month, Guid userId)
    {
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var assets = await _db.FixedAssets
            .Where(a => a.BusinessId == businessId
                        && a.Status == AssetStatus.InUse
                        && a.DepreciationStart <= monthEnd
                        && (a.DepreciatedThrough == null || a.DepreciatedThrough < monthEnd))
            .ToListAsync();

        var lines = new List<DraftLine>();
        var charged = new List<(FixedAsset asset, decimal charge)>();
        foreach (var a in assets)
        {
            var charge = MonthlyCharge(a);
            if (charge <= 0) continue;
            lines.Add(new DraftLine(a.DepExpenseAccountId, charge, 0, $"Depreciation {monthEnd:MMM yyyy} — {a.Description}"));
            lines.Add(new DraftLine(a.AccumDepAccountId, 0, charge, $"Depreciation {monthEnd:MMM yyyy} — {a.Description}"));
            charged.Add((a, charge));
        }
        if (charged.Count == 0) return null;

        var journal = await _posting.CreateDraftAsync(businessId, userId, monthEnd,
            $"DEP-{year}-{month:00}", $"Monthly depreciation run {monthEnd:MMMM yyyy}",
            SourceType.Depreciation, null, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        foreach (var (asset, charge) in charged)
        {
            asset.AccumulatedDepreciation += charge;
            asset.DepreciatedThrough = monthEnd;
        }
        await _db.SaveChangesAsync();
        return journal;
    }

    /// <summary>
    /// Transfers an asset under construction into use:
    /// Dr asset cost account / Cr Assets Under Construction, then starts the depreciation plan.
    /// </summary>
    public async Task<JournalEntry> CapitaliseAsync(Guid businessId, Guid assetId, DateOnly date, Guid userId)
    {
        var asset = await _db.FixedAssets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Asset not found.");
        if (asset.Status != AssetStatus.UnderConstruction)
            throw new InvalidOperationException("Only assets under construction can be capitalised.");
        if (asset.Cost <= 0)
            throw new InvalidOperationException("Set the accumulated cost before capitalising.");

        var auc = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.AssetsUnderConstruction);
        var journal = await _posting.CreateDraftAsync(businessId, userId, date,
            asset.Code, $"Capitalisation of {asset.Description}",
            SourceType.AssetCapitalisation, asset.Id,
            new[]
            {
                new DraftLine(asset.CostAccountId, asset.Cost, 0, $"Capitalise {asset.Description}"),
                new DraftLine(auc.Id, 0, asset.Cost, $"Transfer from AUC — {asset.Description}")
            });
        await _posting.PostAsync(businessId, journal.Id, userId);

        asset.Status = AssetStatus.InUse;
        asset.DepreciationStart = date;
        await _db.SaveChangesAsync();
        return journal;
    }
}
