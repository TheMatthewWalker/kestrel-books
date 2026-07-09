using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Works order costing. The classic manufacturing account flow:
///
///   Issue materials:  Dr WIP                    / Cr RM stock (per component, at AVCO)
///   Complete order:   Dr WIP                    / Cr Direct Labour Absorbed
///                     Dr WIP                    / Cr Production Overhead Absorbed
///                     Dr Finished Goods stock   / Cr WIP (full order cost)
///
/// The finished good re-averages at order cost ÷ quantity completed, so
/// downstream COGS on sale carries material + labour + overhead.
/// </summary>
public class ProductionService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    private readonly StockService _stock;
    public ProductionService(AppDbContext db, PostingService posting, StockService stock)
    {
        _db = db; _posting = posting; _stock = stock;
    }

    public async Task<ProductionOrder> CreateAsync(Guid businessId, Guid itemId, decimal quantity, string? notes)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Item not found.");
        if (!item.TrackStock)
            throw new InvalidOperationException("Enable stock tracking on the item before manufacturing it.");
        if (quantity <= 0) throw new InvalidOperationException("Quantity must be positive.");

        var count = await _db.ProductionOrders.CountAsync(o => o.BusinessId == businessId);
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(), BusinessId = businessId, ItemId = itemId,
            Number = $"WO-{count + 1:0000}", QuantityPlanned = quantity,
            CreatedDate = DateOnly.FromDateTime(DateTime.Today), Notes = notes,
        };
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    /// <summary>Issues BOM components for the planned quantity: Dr WIP / Cr RM stock.</summary>
    public async Task<JournalEntry> IssueMaterialsAsync(Guid businessId, Guid orderId, DateOnly date, Guid userId)
    {
        var order = await _db.ProductionOrders.Include(o => o.Item)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Production order not found.");
        if (order.Status != ProductionStatus.Draft)
            throw new InvalidOperationException("Materials have already been issued for this order.");

        var bom = await _db.BillOfMaterials.Include(b => b.Lines).ThenInclude(l => l.ComponentItem)
            .FirstOrDefaultAsync(b => b.BusinessId == businessId && b.ParentItemId == order.ItemId)
            ?? throw new InvalidOperationException($"No bill of materials defined for {order.Item.Code}.");
        if (bom.Lines.Count == 0)
            throw new InvalidOperationException("The bill of materials has no components.");

        var wip = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.StockWip)).Id;
        var lines = new List<DraftLine>();
        var movements = new List<StockMovement>();
        decimal total = 0;

        foreach (var comp in bom.Lines)
        {
            if (!comp.ComponentItem.TrackStock)
                throw new InvalidOperationException($"Component {comp.ComponentItem.Code} is not stock-tracked.");
            var required = Math.Round(comp.QuantityPer * order.QuantityPlanned, 3);
            var movement = await _stock.MoveAsync(comp.ComponentItem, date,
                StockMovementType.ProductionIssue, -required, null, null, order.Id,
                $"Issued to {order.Number}");
            movements.Add(movement);
            var value = Math.Abs(movement.Value);
            total += value;
            lines.Add(new DraftLine(await _stock.StockAccountIdAsync(comp.ComponentItem), 0, value,
                $"{comp.ComponentItem.Code} × {required} to {order.Number}"));
        }
        lines.Insert(0, new DraftLine(wip, total, 0, $"Materials issued to {order.Number}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, date,
            order.Number, $"Materials issued to works order {order.Number}",
            SourceType.Manual, order.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);
        foreach (var m in movements) m.JournalEntryId = journal.Id;

        order.MaterialCost = total;
        order.MaterialsIssuedDate = date;
        order.Status = ProductionStatus.InProgress;
        await _db.SaveChangesAsync();
        return journal;
    }

    /// <summary>
    /// Completes the order: absorbs labour and overhead into WIP, then transfers
    /// the full order cost from WIP into finished goods stock.
    /// </summary>
    public async Task<JournalEntry> CompleteAsync(Guid businessId, Guid orderId, DateOnly date,
        decimal quantityCompleted, Guid userId)
    {
        var order = await _db.ProductionOrders.Include(o => o.Item)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Production order not found.");
        if (order.Status != ProductionStatus.InProgress)
            throw new InvalidOperationException("Issue materials before completing the order.");
        if (quantityCompleted <= 0)
            throw new InvalidOperationException("Completed quantity must be positive.");

        var bom = await _db.BillOfMaterials
            .FirstOrDefaultAsync(b => b.BusinessId == businessId && b.ParentItemId == order.ItemId);
        var labour = Math.Round((bom?.LabourCostPerUnit ?? 0) * quantityCompleted, 2);
        var overhead = Math.Round((bom?.OverheadCostPerUnit ?? 0) * quantityCompleted, 2);
        var totalCost = order.MaterialCost + labour + overhead;
        var unitCost = Math.Round(totalCost / quantityCompleted, 4);

        var wip = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.StockWip)).Id;
        var fgAccount = await _stock.StockAccountIdAsync(order.Item);
        var lines = new List<DraftLine>();
        if (labour > 0)
        {
            var labAcc = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.LabourAbsorbed)).Id;
            lines.Add(new DraftLine(wip, labour, 0, $"Labour absorbed — {order.Number}"));
            lines.Add(new DraftLine(labAcc, 0, labour, $"Labour absorbed — {order.Number}"));
        }
        if (overhead > 0)
        {
            var ohAcc = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.OverheadAbsorbed)).Id;
            lines.Add(new DraftLine(wip, overhead, 0, $"Overhead absorbed — {order.Number}"));
            lines.Add(new DraftLine(ohAcc, 0, overhead, $"Overhead absorbed — {order.Number}"));
        }
        lines.Add(new DraftLine(fgAccount, totalCost, 0, $"{order.Item.Code} × {quantityCompleted} from {order.Number}"));
        lines.Add(new DraftLine(wip, 0, totalCost, $"Transfer to finished goods — {order.Number}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, date,
            order.Number, $"Completion of works order {order.Number}",
            SourceType.Manual, order.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        var movement = await _stock.MoveAsync(order.Item, date, StockMovementType.ProductionReceipt,
            quantityCompleted, unitCost, journal.Id, order.Id, $"Completed {order.Number}");

        order.LabourCost = labour;
        order.OverheadCost = overhead;
        order.QuantityCompleted = quantityCompleted;
        order.CompletedDate = date;
        order.Status = ProductionStatus.Completed;
        await _db.SaveChangesAsync();
        return journal;
    }
}
