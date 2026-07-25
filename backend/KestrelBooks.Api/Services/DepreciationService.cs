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

    /// <summary>
    /// Takes an asset off the books. The four-legged posting removes what the
    /// balance sheet holds and recognises the difference in the P&amp;L:
    ///   Dr Accumulated depreciation   (clear what has been charged)
    ///   Cr Cost                       (clear the original cost)
    ///   Dr Proceeds account           (bank, or debtor if sold on credit)
    ///   Cr/Dr Profit or loss on disposal (the balancing figure)
    /// Profit on disposal = proceeds − net book value. A scrapping is simply a
    /// disposal with nil proceeds, which writes off the remaining NBV as a loss.
    ///
    /// Note on timing: this uses the accumulated depreciation as it stands, so
    /// run the monthly depreciation up to the disposal month first if you want
    /// the part-year charge included — the caller is told when that is the case.
    /// </summary>
    public async Task<(JournalEntry journal, decimal gainLoss, bool depreciationBehind)> DisposeAsync(
        Guid businessId, Guid assetId, DateOnly disposalDate, decimal proceeds,
        Guid proceedsAccountId, Guid disposalAccountId, Guid userId)
    {
        var asset = await _db.FixedAssets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Asset not found.");
        if (asset.Status == AssetStatus.Disposed)
            throw new InvalidOperationException("This asset has already been disposed.");
        if (proceeds < 0)
            throw new InvalidOperationException("Proceeds cannot be negative.");
        if (disposalDate < asset.AcquisitionDate)
            throw new InvalidOperationException("An asset cannot be disposed of before it was acquired.");

        var nbv = asset.Cost - asset.AccumulatedDepreciation;
        var gainLoss = decimal.Round(proceeds - nbv, 2, MidpointRounding.AwayFromZero);

        // Was depreciation charged up to the month of disposal?
        var disposalMonthStart = new DateOnly(disposalDate.Year, disposalDate.Month, 1);
        var depreciationBehind = asset.Status == AssetStatus.InUse
            && (asset.DepreciatedThrough is null || asset.DepreciatedThrough < disposalMonthStart);

        var lines = new List<DraftLine>();
        if (asset.AccumulatedDepreciation != 0)
            lines.Add(new DraftLine(asset.AccumDepAccountId, asset.AccumulatedDepreciation, 0,
                $"Remove accumulated depreciation — {asset.Code}"));
        lines.Add(new DraftLine(asset.CostAccountId, 0, asset.Cost, $"Remove cost — {asset.Code}"));
        if (proceeds != 0)
            lines.Add(new DraftLine(proceedsAccountId, proceeds, 0, $"Disposal proceeds — {asset.Code}"));
        if (gainLoss > 0)
            lines.Add(new DraftLine(disposalAccountId, 0, gainLoss, $"Profit on disposal — {asset.Code}"));
        else if (gainLoss < 0)
            lines.Add(new DraftLine(disposalAccountId, -gainLoss, 0, $"Loss on disposal — {asset.Code}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, disposalDate,
            asset.Code, $"Disposal of {asset.Description}", SourceType.AssetDisposal, asset.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        asset.Status = AssetStatus.Disposed;
        asset.DisposalDate = disposalDate;
        asset.DisposalProceeds = proceeds;
        asset.DisposalGainLoss = gainLoss;
        await _db.SaveChangesAsync();
        return (journal, gainLoss, depreciationBehind);
    }
}
