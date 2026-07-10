using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>AVCO golden figures — the AAT Level 3 worked example, automated.</summary>
public class InventoryCostingTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _itemId;

    public InventoryCostingTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Stock Ltd");
        _businessId = b.Id;
        var item = new Item
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, Kind = ItemKind.RawMaterial,
            Code = "RM-1", Name = "Steel", TrackStock = true,
        };
        ctx.Items.Add(item);
        ctx.SaveChanges();
        _itemId = item.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    [Fact]
    public async Task Avco_ReAveragesOnReceipt_IssuesAtAverage()
    {
        using var ctx = _db.Create();
        var stock = new StockService(ctx, new PostingService(ctx));
        var item = await ctx.Items.FirstAsync(i => i.Id == _itemId);

        // Receive 10 @ £5.00 → avg £5.00
        await stock.MoveAsync(item, new DateOnly(2026, 3, 1), StockMovementType.PurchaseReceipt, 10, 5m, null, null, null);
        Assert.Equal(5m, item.AvgUnitCost);

        // Receive 20 @ £8.00 → avg (10×5 + 20×8)/30 = £7.00
        await stock.MoveAsync(item, new DateOnly(2026, 3, 5), StockMovementType.PurchaseReceipt, 20, 8m, null, null, null);
        Assert.Equal(7m, item.AvgUnitCost);
        Assert.Equal(30, item.QuantityOnHand);

        // Issue 5 → value 5×£7 = £35 out; average unchanged; qty 25
        var issue = await stock.MoveAsync(item, new DateOnly(2026, 3, 8), StockMovementType.SaleIssue, -5, null, null, null, null);
        Assert.Equal(-35m, issue.Value);
        Assert.Equal(7m, item.AvgUnitCost);
        Assert.Equal(25, item.QuantityOnHand);
        Assert.Equal(25, issue.QuantityAfter);
    }

    [Fact]
    public async Task Issue_BeyondStock_IsBlocked()
    {
        using var ctx = _db.Create();
        var stock = new StockService(ctx, new PostingService(ctx));
        var item = await ctx.Items.FirstAsync(i => i.Id == _itemId);
        await stock.MoveAsync(item, new DateOnly(2026, 3, 1), StockMovementType.PurchaseReceipt, 3, 10m, null, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stock.MoveAsync(item, new DateOnly(2026, 3, 2), StockMovementType.SaleIssue, -4, null, null, null, null));
        Assert.Equal(3, item.QuantityOnHand); // unchanged after the failed issue
    }

    [Fact]
    public async Task WriteOff_Posts_DrAdjustments_CrStock_AtAverage()
    {
        using var ctx = _db.Create();
        var posting = new PostingService(ctx);
        var stock = new StockService(ctx, posting);
        var item = await ctx.Items.FirstAsync(i => i.Id == _itemId);
        await stock.MoveAsync(item, new DateOnly(2026, 3, 1), StockMovementType.PurchaseReceipt, 10, 6m, null, null, null);
        await ctx.SaveChangesAsync();

        var journal = await stock.AdjustAsync(_businessId, _itemId, new DateOnly(2026, 3, 31), -2, null, "count variance", _user);

        var adjAcc = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.StockAdjustments);
        var rmAcc = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.StockRawMaterials);
        Assert.Equal(12m, journal.Lines.First(l => l.AccountId == adjAcc.Id).Debit);  // 2 × £6
        Assert.Equal(12m, journal.Lines.First(l => l.AccountId == rmAcc.Id).Credit);
        Assert.Equal(8, item.QuantityOnHand);
    }

    public void Dispose() => _db.Dispose();
}
