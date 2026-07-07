namespace KestrelBooks.Api.Domain;

public enum AssetStatus { UnderConstruction = 0, InUse = 1, Disposed = 2 }
public enum DepreciationMethod { StraightLine = 0, ReducingBalance = 1 }

/// <summary>
/// Fixed asset register entry. Assets under construction accumulate cost in the AUC account;
/// capitalisation transfers cost to the asset cost account and starts the depreciation plan.
/// Monthly depreciation runs post Dr depreciation expense / Cr accumulated depreciation.
/// </summary>
public class FixedAsset
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Category { get; set; }            // e.g. Plant & Machinery, Motor Vehicles
    public AssetStatus Status { get; set; } = AssetStatus.InUse;
    public DateOnly AcquisitionDate { get; set; }
    public decimal Cost { get; set; }
    public decimal ResidualValue { get; set; }
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    /// <summary>Straight line: months of useful life.</summary>
    public int UsefulLifeMonths { get; set; } = 60;
    /// <summary>Reducing balance: annual percentage, e.g. 25 for 25%.</summary>
    public decimal AnnualRatePercent { get; set; }
    public DateOnly DepreciationStart { get; set; }
    /// <summary>Last month-end depreciation has been posted through (null = never).</summary>
    public DateOnly? DepreciatedThrough { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public Guid CostAccountId { get; set; }
    public Guid AccumDepAccountId { get; set; }
    public Guid DepExpenseAccountId { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public string? Notes { get; set; }

    public decimal NetBookValue => Cost - AccumulatedDepreciation;
}
