using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Perpetual inventory engine using weighted average cost (AVCO):
///   receipt:  new avg = (qty on hand × avg + qty in × unit cost) ÷ (qty on hand + qty in)
///   issue:    goes out at the current average; the average is unchanged.
/// Every change is recorded as a StockMovement with a running balance,
/// so the item card can always be reconciled to the stock accounts.
/// </summary>
public class StockService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public StockService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    /// <summary>
    /// Idempotently creates the manufacturing accounts for businesses seeded
    /// before v1.2 (RM/WIP/FG stock, COGS, absorption and adjustment accounts).
    /// </summary>
    public async Task EnsureManufacturingAccountsAsync(Guid businessId)
    {
        var wanted = new (string code, string name, AccountType type, string sub, string tag)[]
        {
            ("1001", "Stock — Raw Materials", AccountType.Asset, "Current Assets", SystemTags.StockRawMaterials),
            ("1002", "Stock — Work in Progress", AccountType.Asset, "Current Assets", SystemTags.StockWip),
            ("1003", "Stock — Finished Goods", AccountType.Asset, "Current Assets", SystemTags.StockFinishedGoods),
            ("5001", "Cost of Goods Sold", AccountType.Expense, "Cost of Sales", SystemTags.CostOfGoodsSold),
            ("5200", "Direct Labour Absorbed", AccountType.Expense, "Cost of Sales", SystemTags.LabourAbsorbed),
            ("5300", "Production Overhead Absorbed", AccountType.Expense, "Cost of Sales", SystemTags.OverheadAbsorbed),
            ("5400", "Stock Adjustments & Write-offs", AccountType.Expense, "Cost of Sales", SystemTags.StockAdjustments),
        };
        var existingTags = await _db.Accounts
            .Where(a => a.BusinessId == businessId && a.SystemTag != null)
            .Select(a => a.SystemTag!).ToListAsync();
        var existingCodes = await _db.Accounts
            .Where(a => a.BusinessId == businessId).Select(a => a.Code).ToListAsync();
        var codes = existingCodes.ToHashSet();

        foreach (var w in wanted.Where(w => !existingTags.Contains(w.tag)))
        {
            var code = w.code;
            while (codes.Contains(code)) code = (int.Parse(code) + 1).ToString("0000");
            codes.Add(code);
            _db.Accounts.Add(new Account
            {
                Id = Guid.NewGuid(), BusinessId = businessId, Code = code, Name = w.name,
                Type = w.type, SubType = w.sub, SystemTag = w.tag
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Records a movement and maintains the item's AVCO and quantity. Positive qty = in.</summary>
    public async Task<StockMovement> MoveAsync(Item item, DateOnly date, StockMovementType type,
        decimal quantity, decimal? unitCostForReceipt, Guid? journalId, Guid? sourceId, string? notes)
    {
        if (quantity == 0) throw new InvalidOperationException("Movement quantity cannot be zero.");
        decimal unitCost;
        if (quantity > 0)
        {
            unitCost = unitCostForReceipt
                ?? throw new InvalidOperationException("A unit cost is required for stock receipts.");
            var newQty = item.QuantityOnHand + quantity;
            item.AvgUnitCost = newQty == 0 ? 0
                : Math.Round((item.QuantityOnHand * item.AvgUnitCost + quantity * unitCost) / newQty, 4);
            item.QuantityOnHand = newQty;
        }
        else
        {
            if (item.QuantityOnHand + quantity < 0)
                throw new InvalidOperationException(
                    $"Insufficient stock of {item.Code}: on hand {item.QuantityOnHand}, requested {-quantity}.");
            unitCost = item.AvgUnitCost;
            item.QuantityOnHand += quantity;
        }

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(), BusinessId = item.BusinessId, ItemId = item.Id,
            Date = date, Type = type, Quantity = quantity, UnitCost = unitCost,
            Value = Math.Round(quantity * unitCost, 2), QuantityAfter = item.QuantityOnHand,
            JournalEntryId = journalId, SourceId = sourceId, Notes = notes,
        };
        _db.StockMovements.Add(movement);
        return movement;
    }

    /// <summary>Resolves the stock (asset) account for a tracked item.</summary>
    public async Task<Guid> StockAccountIdAsync(Item item)
    {
        if (item.InventoryAccountId is Guid id) return id;
        var tag = item.Kind == ItemKind.FinishedGood ? SystemTags.StockFinishedGoods : SystemTags.StockRawMaterials;
        return (await _posting.RequireTaggedAccountAsync(item.BusinessId, tag)).Id;
    }

    public async Task<Guid> CogsAccountIdAsync(Item item) =>
        item.CogsAccountId
        ?? (await _posting.RequireTaggedAccountAsync(item.BusinessId, SystemTags.CostOfGoodsSold)).Id;

    /// <summary>
    /// Stock count adjustment: positive quantity writes stock up
    /// (Dr stock / Cr adjustments), negative writes off (Dr adjustments / Cr stock).
    /// </summary>
    public async Task<JournalEntry> AdjustAsync(Guid businessId, Guid itemId, DateOnly date,
        decimal quantity, decimal? unitCostForIncrease, string reason, Guid userId)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Item not found.");
        if (!item.TrackStock) throw new InvalidOperationException("Item is not stock-tracked.");

        var movement = await MoveAsync(item, date, StockMovementType.Adjustment, quantity,
            unitCostForIncrease ?? item.AvgUnitCost, null, null, reason);

        var stockAcc = await StockAccountIdAsync(item);
        var adjAcc = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.StockAdjustments)).Id;
        var value = Math.Abs(movement.Value);
        var lines = movement.Value >= 0
            ? new[] { new DraftLine(stockAcc, value, 0, reason), new DraftLine(adjAcc, 0, value, reason) }
            : new[] { new DraftLine(adjAcc, value, 0, reason), new DraftLine(stockAcc, 0, value, reason) };

        var journal = await _posting.CreateDraftAsync(businessId, userId, date,
            $"ADJ-{item.Code}", $"Stock adjustment — {item.Code}: {reason}",
            SourceType.Manual, item.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);
        movement.JournalEntryId = journal.Id;
        await _db.SaveChangesAsync();
        return journal;
    }
}
