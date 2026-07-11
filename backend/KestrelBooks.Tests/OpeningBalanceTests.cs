using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class OpeningBalanceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _bank, _debtors, _retained;
    private Guid _customerId;

    public OpeningBalanceTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, _, bank, _) = TestDb.SeedBusiness(ctx, "Convert Ltd");
        _businessId = b.Id; _bank = bank.Id; _debtors = debtors.Id;
        _retained = ctx.Accounts.First(a => a.SystemTag == SystemTags.RetainedEarnings).Id;
        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Legacy Cust" };
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        _customerId = customer.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    [Fact]
    public async Task TrialBalance_PostsOnce_DatedDayBeforeConversion()
    {
        using var ctx = _db.Create();
        var svc = new OpeningBalanceService(ctx, new PostingService(ctx));
        var conversion = new DateOnly(2026, 4, 1);

        var journal = await svc.ImportTrialBalanceAsync(_businessId, conversion, new[]
        {
            new TbLine(_bank, 5000, 0),
            new TbLine(_debtors, 2400, 0),
            new TbLine(_retained, 0, 7400),
        }, _user);

        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.Equal(new DateOnly(2026, 3, 31), journal.Date);
        Assert.Equal(SourceType.OpeningBalance, journal.Source);

        // A second conversion must be blocked while the first stands.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ImportTrialBalanceAsync(_businessId, conversion,
                new[] { new TbLine(_bank, 1, 0), new TbLine(_retained, 0, 1) }, _user));
    }

    [Fact]
    public async Task UnbalancedTb_IsRejected_ByTheEngine()
    {
        using var ctx = _db.Create();
        var svc = new OpeningBalanceService(ctx, new PostingService(ctx));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ImportTrialBalanceAsync(_businessId, new DateOnly(2026, 4, 1),
                new[] { new TbLine(_bank, 100, 0), new TbLine(_retained, 0, 90) }, _user));
    }

    [Fact]
    public async Task OpeningInvoice_HasNoJournal_ButSettlesNormally()
    {
        using var ctx = _db.Create();
        var posting = new PostingService(ctx);
        var opening = new OpeningBalanceService(ctx, posting);
        var docs = new DocumentPostingService(ctx, posting, new StockService(ctx, posting));

        var invId = await opening.AddOpeningSalesInvoiceAsync(_businessId,
            new OpeningInvoiceRequest(_customerId, "LEGACY-042", new DateOnly(2026, 3, 10),
                new DateOnly(2026, 4, 9), 480m));

        var inv = await ctx.SalesInvoices.FirstAsync(i => i.Id == invId);
        Assert.Equal(DocumentStatus.Posted, inv.Status);
        Assert.Null(inv.JournalEntryId);          // no journal — TB carries the value
        Assert.True(inv.IsOpeningBalance);

        // Receipt against it posts Dr Bank / Cr Debtors as normal.
        var tx = new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = MoneyDirection.In,
            Date = new DateOnly(2026, 4, 15), Reference = "BACS", Amount = 480m,
            BankAccountId = _bank, SalesInvoiceId = invId,
        };
        ctx.MoneyTransactions.Add(tx);
        await ctx.SaveChangesAsync();
        var journal = await docs.PostMoneyTransactionAsync(_businessId, tx.Id, _user);

        Assert.Equal(480m, journal.Lines.First(l => l.AccountId == _bank).Debit);
        Assert.Equal(480m, journal.Lines.First(l => l.AccountId == _debtors).Credit);
        Assert.Equal(480m, (await ctx.SalesInvoices.FirstAsync(i => i.Id == invId)).AmountPaid);
    }

    [Fact]
    public async Task OpeningStock_SetsQuantityAndAvco_OnlyOnEmptyItems()
    {
        using var ctx = _db.Create();
        var posting = new PostingService(ctx);
        var opening = new OpeningBalanceService(ctx, posting);
        var stock = new StockService(ctx, posting);
        var item = new Item
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Kind = ItemKind.RawMaterial,
            Code = "RM-9", Name = "Widgets", TrackStock = true,
        };
        ctx.Items.Add(item);
        await ctx.SaveChangesAsync();

        await opening.SetOpeningStockAsync(_businessId, new DateOnly(2026, 4, 1),
            new[] { new OpeningStockLine(item.Id, 40, 2.50m) }, stock);

        Assert.Equal(40, item.QuantityOnHand);
        Assert.Equal(2.50m, item.AvgUnitCost);
        // Second attempt on a non-empty item is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            opening.SetOpeningStockAsync(_businessId, new DateOnly(2026, 4, 1),
                new[] { new OpeningStockLine(item.Id, 10, 3m) }, stock));
    }

    public void Dispose() => _db.Dispose();
}
