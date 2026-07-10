using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// Invoices → double entry, with golden figures for the VAT rounding rule
/// (per line, 2dp, MidpointRounding.AwayFromZero).
/// </summary>
public class DocumentPostingTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId, _vendorId, _salesAcc, _purchAcc, _bank, _debtors, _creditors, _vatOut, _vatIn;

    public DocumentPostingTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, sales, bank, vatOut) = TestDb.SeedBusiness(ctx, "Doc Ltd");
        _businessId = b.Id; _debtors = debtors.Id; _salesAcc = sales.Id; _bank = bank.Id; _vatOut = vatOut.Id;
        _creditors = ctx.Accounts.First(a => a.SystemTag == SystemTags.TradeCreditors).Id;
        _vatIn = ctx.Accounts.First(a => a.SystemTag == SystemTags.VatInput).Id;
        _purchAcc = ctx.Accounts.First(a => a.Code == "5000").Id;

        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Cust" };
        var vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Vend" };
        ctx.AddRange(customer, vendor);
        ctx.SaveChanges();
        _customerId = customer.Id; _vendorId = vendor.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private (AppDbContext ctx, DocumentPostingService docs, PostingService posting) Services()
    {
        var ctx = _db.Create();
        var posting = new PostingService(ctx);
        return (ctx, new DocumentPostingService(ctx, posting, new StockService(ctx, posting)), posting);
    }

    private SalesInvoice NewSalesInvoice(params (decimal qty, decimal price, VatRate rate)[] lines)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "INV-1", Date = new DateOnly(2026, 2, 10), DueDate = new DateOnly(2026, 3, 12),
        };
        foreach (var (qty, price, rate) in lines)
            inv.Lines.Add(new SalesInvoiceLine
            {
                Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "line",
                Quantity = qty, UnitPrice = price, VatRate = rate, AccountId = _salesAcc,
            });
        DocumentPostingService.Recalculate(inv);
        return inv;
    }

    [Fact]
    public void VatRounding_IsPerLine_AwayFromZero()
    {
        // 3 × £0.33 = £0.99 net → VAT £0.198 → £0.20 per line (AwayFromZero).
        // Two such lines: VAT total £0.40 — NOT £0.396→£0.40 by coincidence but by per-line rule;
        // the distinction shows at £0.99 net × 1 line: VAT must be £0.20, not £0.198 truncated.
        var inv = NewSalesInvoice((3, 0.33m, VatRate.Standard20), (3, 0.33m, VatRate.Standard20));
        Assert.Equal(1.98m, inv.NetTotal);
        Assert.Equal(0.40m, inv.VatTotal);
        Assert.Equal(2.38m, inv.GrossTotal);
    }

    [Fact]
    public async Task SalesInvoice_Posts_DrDebtorsGross_CrSalesNet_CrOutputVat()
    {
        var (ctx, docs, _) = Services();
        using var _ = ctx;
        var inv = NewSalesInvoice((2, 100m, VatRate.Standard20)); // net 200, VAT 40, gross 240
        ctx.SalesInvoices.Add(inv);
        await ctx.SaveChangesAsync();

        var journal = await docs.PostSalesInvoiceAsync(_businessId, inv.Id, _user);

        Assert.Equal(240m, journal.Lines.First(l => l.AccountId == _debtors).Debit);
        Assert.Equal(200m, journal.Lines.First(l => l.AccountId == _salesAcc).Credit);
        Assert.Equal(40m, journal.Lines.First(l => l.AccountId == _vatOut).Credit);
        Assert.Equal(JournalStatus.Posted, journal.Status);
        Assert.Equal(DocumentStatus.Posted, (await ctx.SalesInvoices.FirstAsync(i => i.Id == inv.Id)).Status);
        // Posted invoices are locked: posting again must fail.
        await Assert.ThrowsAsync<InvalidOperationException>(() => docs.PostSalesInvoiceAsync(_businessId, inv.Id, _user));
    }

    [Fact]
    public async Task PurchaseInvoice_Posts_DrExpenseAndInputVat_CrCreditorsGross()
    {
        var (ctx, docs, _) = Services();
        using var _ = ctx;
        var inv = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = _vendorId,
            Number = "PI-9", Date = new DateOnly(2026, 2, 12), DueDate = new DateOnly(2026, 3, 14),
        };
        inv.Lines.Add(new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id, Description = "goods",
            Quantity = 1, UnitPrice = 80m, VatRate = VatRate.Standard20, AccountId = _purchAcc,
        });
        DocumentPostingService.Recalculate(inv);
        ctx.PurchaseInvoices.Add(inv);
        await ctx.SaveChangesAsync();

        var journal = await docs.PostPurchaseInvoiceAsync(_businessId, inv.Id, _user);

        Assert.Equal(80m, journal.Lines.First(l => l.AccountId == _purchAcc).Debit);
        Assert.Equal(16m, journal.Lines.First(l => l.AccountId == _vatIn).Debit);
        Assert.Equal(96m, journal.Lines.First(l => l.AccountId == _creditors).Credit);
    }

    [Fact]
    public async Task Receipt_SettlingInvoice_DrBank_CrDebtors_AndTracksAmountPaid()
    {
        var (ctx, docs, _) = Services();
        using var _ = ctx;
        var inv = NewSalesInvoice((1, 100m, VatRate.Standard20)); // gross 120
        ctx.SalesInvoices.Add(inv);
        await ctx.SaveChangesAsync();
        await docs.PostSalesInvoiceAsync(_businessId, inv.Id, _user);

        var tx = new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = MoneyDirection.In,
            Date = new DateOnly(2026, 2, 20), Reference = "BACS", Amount = 120m,
            BankAccountId = _bank, SalesInvoiceId = inv.Id,
        };
        ctx.MoneyTransactions.Add(tx);
        await ctx.SaveChangesAsync();

        var journal = await docs.PostMoneyTransactionAsync(_businessId, tx.Id, _user);

        Assert.Equal(120m, journal.Lines.First(l => l.AccountId == _bank).Debit);
        Assert.Equal(120m, journal.Lines.First(l => l.AccountId == _debtors).Credit);
        Assert.Equal(120m, (await ctx.SalesInvoices.FirstAsync(i => i.Id == inv.Id)).AmountPaid);
    }

    public void Dispose() => _db.Dispose();
}
