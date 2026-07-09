namespace KestrelBooks.Api.Domain;

public enum StockMovementType
{
    PurchaseReceipt = 0,   // goods received from a purchase invoice
    SaleIssue = 1,         // stock issued on a sales invoice (COGS)
    ProductionIssue = 2,   // raw materials issued to a production order (into WIP)
    ProductionReceipt = 3, // finished goods received from a completed order
    Adjustment = 4         // count corrections, write-offs
}

/// <summary>
/// The perpetual inventory audit trail. Every quantity change is a movement;
/// item QuantityOnHand and AvgUnitCost are maintained by StockService using
/// weighted average cost (AVCO): receipts re-average, issues go out at the
/// current average.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public DateOnly Date { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }       // signed: + in, − out
    public decimal UnitCost { get; set; }       // cost applied to this movement
    public decimal Value { get; set; }          // Quantity × UnitCost (signed)
    public decimal QuantityAfter { get; set; }  // running balance for the item
    public Guid? JournalEntryId { get; set; }
    public Guid? SourceId { get; set; }         // invoice / production order id
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Bill of materials for a manufactured item: the components consumed per unit,
/// plus per-unit labour and overhead absorption rates applied on completion.
/// </summary>
public class BillOfMaterial
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ParentItemId { get; set; }      // the finished good
    public decimal LabourCostPerUnit { get; set; }
    public decimal OverheadCostPerUnit { get; set; }
    public List<BomLine> Lines { get; set; } = new();
}

public class BomLine
{
    public Guid Id { get; set; }
    public Guid BillOfMaterialId { get; set; }
    public Guid ComponentItemId { get; set; }
    public Item ComponentItem { get; set; } = null!;
    public decimal QuantityPer { get; set; }    // component units per one parent unit
}

public enum ProductionStatus { Draft = 0, InProgress = 1, Completed = 2, Cancelled = 3 }

/// <summary>
/// A works order. Costs flow: issue materials (Dr WIP / Cr RM stock at AVCO),
/// complete (Dr WIP / Cr labour + overhead absorbed for the absorption element,
/// then Dr FG stock / Cr WIP for the full order cost). The finished good's
/// average cost re-averages with total order cost ÷ quantity completed.
/// </summary>
public class ProductionOrder
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Number { get; set; } = "";
    public Guid ItemId { get; set; }            // finished good being made
    public Item Item { get; set; } = null!;
    public decimal QuantityPlanned { get; set; }
    public ProductionStatus Status { get; set; } = ProductionStatus.Draft;
    public DateOnly CreatedDate { get; set; }
    public DateOnly? MaterialsIssuedDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public decimal QuantityCompleted { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LabourCost { get; set; }
    public decimal OverheadCost { get; set; }
    public string? Notes { get; set; }

    public decimal TotalCost => MaterialCost + LabourCost + OverheadCost;
}
