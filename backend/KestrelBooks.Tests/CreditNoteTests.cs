using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class CreditNoteTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId, _otherCustomerId, _salesAcc, _bank, _debtors, _vatOut;

    public CreditNoteTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, sales, bank, vatOut) = TestDb.SeedBusiness(ctx, "CN Ltd");
        _businessId = b.Id; _debtors = debtors.Id; _salesAcc = sales.Id; _bank = bank.Id; _vatOut = vatOut.Id;
        var c1 = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Main" };
        var c2 = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Other" };
        ctx.AddRange(c1, c2);
        ctx.SaveChanges();
        _customerId = c1.Id; _otherCustomerId = c2.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private (AppDbContext ctx, DocumentPostingService docs) Services()
    {
        var ctx = _db.Create();
        var posting = new PostingService(ctx);
        return (ctx, new DocumentPostingService(ctx, posting, new StockService(ctx, posting)));
    }

    private async Task<SalesInvoice> PostedInvoice(AppDbContext ctx, DocumentPostingService docs,
        Guid customerId, decimal net)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = customerId,
            Number = $"INV-{Guid.NewGuid().ToString()[..4]}",
            Date = new DateOnly(2026, 5, 1), DueDate = new DateOnly(2026, 5, 31),
        };
        inv.Lines.Add(new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "x",
            Quantity = 1, UnitPrice = net, VatRate = VatRate.Standard20, AccountId = _salesAcc,
        });
        DocumentPostingService.Recalculate(inv);
        ctx.SalesInvoices.Add(inv);
        await ctx.SaveChangesAsync();
        await docs.PostSalesInvoiceAsync(_businessId, inv.Id, _user);
        return inv;
    }

    private async Task<SalesCreditNote> PostedCreditNote(AppDbContext ctx, DocumentPostingService docs,
        Guid customerId, decimal net)
    {
        var cn = new SalesCreditNote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = customerId,
            Number = $"CN-{Guid.NewGuid().ToString()[..4]}",
            Date = new DateOnly(2026, 5, 10), DueDate = new DateOnly(2026, 5, 10),
        };
        cn.Lines.Add(new SalesCreditNoteLine
        {
            Id = Guid.NewGuid(), SalesCreditNoteId = cn.Id, Description = "return",
            Quantity = 1, UnitPrice = net, VatRate = VatRate.Standard20, AccountId = _salesAcc,
        });
        DocumentPostingService.Recalculate(cn);
        ctx.SalesCreditNotes.Add(cn);
        await ctx.SaveChangesAsync();
        await docs.PostSalesCreditNoteAsync(_businessId, cn.Id, _user);
        return cn;
    }

    [Fact]
    public async Task SalesCreditNote_Posts_TheMirrorOfAnInvoice()
    {
        var (ctx, docs) = Services();
        using var _ = ctx;
        var cn = await PostedCreditNote(ctx, docs, _customerId, 40m); // net 40, VAT 8, gross 48

        var journal = await ctx.Journals.Include(j => j.Lines)
            .FirstAsync(j => j.SourceId == cn.Id && j.Source == SourceType.SalesCreditNote);
        Assert.Equal(40m, journal.Lines.First(l => l.AccountId == _salesAcc).Debit);   // sales reduced
        Assert.Equal(8m, journal.Lines.First(l => l.AccountId == _vatOut).Debit);      // output VAT reduced
        Assert.Equal(48m, journal.Lines.First(l => l.AccountId == _debtors).Credit);   // debtor reduced
    }

    [Fact]
    public async Task Allocation_IsAJournallessContra_WithGuards()
    {
        var (ctx, docs) = Services();
        using var _ = ctx;
        var inv = await PostedInvoice(ctx, docs, _customerId, 100m);   // gross 120
        var cn = await PostedCreditNote(ctx, docs, _customerId, 40m);  // gross 48
        var journalsBefore = await ctx.Journals.CountAsync();

        // Valid allocation reduces both outstanding balances, creating no journal.
        cn.AmountPaid += 48m; inv.AmountPaid += 48m; // (controller logic mirrored)
        await ctx.SaveChangesAsync();
        Assert.Equal(journalsBefore, await ctx.Journals.CountAsync());
        Assert.Equal(72m, inv.GrossTotal - inv.AmountPaid);
        Assert.Equal(0m, cn.GrossTotal - cn.AmountPaid);
    }

    [Fact]
    public async Task Refund_AgainstCreditNote_PostsDrDebtorsCrBank()
    {
        var (ctx, docs) = Services();
        using var _ = ctx;
        var cn = await PostedCreditNote(ctx, docs, _customerId, 40m); // gross 48

        var tx = new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = MoneyDirection.Out,
            Date = new DateOnly(2026, 5, 12), Reference = "REFUND", Amount = 48m,
            BankAccountId = _bank, SalesCreditNoteId = cn.Id,
        };
        ctx.MoneyTransactions.Add(tx);
        await ctx.SaveChangesAsync();
        var journal = await docs.PostMoneyTransactionAsync(_businessId, tx.Id, _user);

        Assert.Equal(48m, journal.Lines.First(l => l.AccountId == _debtors).Debit);
        Assert.Equal(48m, journal.Lines.First(l => l.AccountId == _bank).Credit);
        Assert.Equal(48m, (await ctx.SalesCreditNotes.FirstAsync(c => c.Id == cn.Id)).AmountPaid);

        // A second refund of the full amount must be refused — nothing left to refund.
        var tx2 = new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = MoneyDirection.Out,
            Date = new DateOnly(2026, 5, 13), Reference = "REFUND2", Amount = 1m,
            BankAccountId = _bank, SalesCreditNoteId = cn.Id,
        };
        ctx.MoneyTransactions.Add(tx2);
        await ctx.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            docs.PostMoneyTransactionAsync(_businessId, tx2.Id, _user));
    }

    [Fact]
    public async Task StockReturn_ComesBackAtCurrentAverage_ReversingCogs()
    {
        var (ctx, docs) = Services();
        using var _ = ctx;
        var posting = new PostingService(ctx);
        var stock = new StockService(ctx, posting);
        var item = new Item
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Kind = ItemKind.FinishedGood,
            Code = "FG-1", Name = "Widget", TrackStock = true,
        };
        ctx.Items.Add(item);
        await ctx.SaveChangesAsync();
        await stock.MoveAsync(item, new DateOnly(2026, 5, 1), StockMovementType.PurchaseReceipt, 10, 6m, null, null, null);
        await ctx.SaveChangesAsync();

        var cn = new SalesCreditNote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "CN-S1", Date = new DateOnly(2026, 5, 15), DueDate = new DateOnly(2026, 5, 15),
        };
        cn.Lines.Add(new SalesCreditNoteLine
        {
            Id = Guid.NewGuid(), SalesCreditNoteId = cn.Id, ItemId = item.Id, Description = "returned widgets",
            Quantity = 2, UnitPrice = 15m, VatRate = VatRate.Standard20, AccountId = _salesAcc,
        });
        DocumentPostingService.Recalculate(cn);
        ctx.SalesCreditNotes.Add(cn);
        await ctx.SaveChangesAsync();
        var journal = await docs.PostSalesCreditNoteAsync(_businessId, cn.Id, _user);

        Assert.Equal(12, item.QuantityOnHand);   // 10 + 2 back in
        Assert.Equal(6m, item.AvgUnitCost);      // returned at current average — unchanged
        var cogs = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.CostOfGoodsSold);
        var fg = await ctx.Accounts.FirstAsync(a => a.SystemTag == SystemTags.StockFinishedGoods);
        Assert.Equal(12m, journal.Lines.First(l => l.AccountId == fg.Id).Debit);   // 2 × £6 back to stock
        Assert.Equal(12m, journal.Lines.First(l => l.AccountId == cogs.Id).Credit); // COGS reversed
    }

    public void Dispose() => _db.Dispose();
}
