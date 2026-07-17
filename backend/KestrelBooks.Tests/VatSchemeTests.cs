using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// Cash accounting and Flat Rate Scheme golden figures. SQLite-backed on
/// purpose: the scheme computations aggregate client-side, and these tests
/// prove that stays true.
/// </summary>
public class VatSchemeTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private Guid _customerId, _vendorId, _bank;

    public VatSchemeTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, bank, _) = TestDb.SeedBusiness(ctx, "Scheme Ltd");
        _businessId = b.Id; _bank = bank.Id;
        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "C" };
        var vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "V" };
        ctx.AddRange(customer, vendor);
        ctx.SaveChanges();
        _customerId = customer.Id; _vendorId = vendor.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private async Task SetScheme(AppDbContext ctx, VatScheme scheme, decimal rate = 0)
    {
        var b = await ctx.Businesses.FirstAsync(x => x.Id == _businessId);
        b.VatScheme = scheme;
        b.FlatRatePercent = rate;
        await ctx.SaveChangesAsync();
    }

    private Guid AddSalesInvoice(AppDbContext ctx, decimal net, decimal vat, DateOnly date)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = $"I{Guid.NewGuid().ToString()[..4]}", Date = date, DueDate = date.AddDays(30),
            NetTotal = net, VatTotal = vat, GrossTotal = net + vat, Status = DocumentStatus.Posted,
        };
        ctx.SalesInvoices.Add(inv);
        return inv.Id;
    }

    private void AddMoney(AppDbContext ctx, MoneyDirection dir, decimal amount, DateOnly date,
        Guid? salesInvId = null, Guid? purchInvId = null)
        => ctx.MoneyTransactions.Add(new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = dir, Date = date,
            Reference = "T", Amount = amount, BankAccountId = _bank,
            SalesInvoiceId = salesInvId, PurchaseInvoiceId = purchInvId,
            Status = DocumentStatus.Posted,
        });

    [Fact]
    public async Task CashScheme_ApportionsPartPayments_ByVatToGrossRatio()
    {
        using var ctx = _db.Create();
        await SetScheme(ctx, VatScheme.CashAccounting);

        // Invoice net 100 / VAT 20 / gross 120, raised in March (outside the quarter).
        var invId = AddSalesInvoice(ctx, 100, 20, new DateOnly(2026, 3, 10));
        // Purchase invoice net 40 / VAT 8 / gross 48, raised in March, fully paid in May.
        var pi = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = _vendorId,
            Number = "P1", Date = new DateOnly(2026, 3, 12), DueDate = new DateOnly(2026, 4, 11),
            NetTotal = 40, VatTotal = 8, GrossTotal = 48, Status = DocumentStatus.Posted,
        };
        ctx.PurchaseInvoices.Add(pi);
        // Half the sales invoice (£60) received 10 May; £48 paid out 12 May.
        AddMoney(ctx, MoneyDirection.In, 60, new DateOnly(2026, 5, 10), salesInvId: invId);
        AddMoney(ctx, MoneyDirection.Out, 48, new DateOnly(2026, 5, 12), purchInvId: pi.Id);
        // A payment outside the quarter must not count.
        AddMoney(ctx, MoneyDirection.In, 60, new DateOnly(2026, 8, 1), salesInvId: invId);
        await ctx.SaveChangesAsync();

        var boxes = await new VatReturnService(ctx, null!).ComputeAsync(
            _businessId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        // £60 of a 120-gross invoice carries 60 × 20/120 = £10 VAT and £50 net.
        Assert.Equal(10m, boxes.VatDueSales);
        Assert.Equal(8m, boxes.VatReclaimedCurrPeriod);
        Assert.Equal(2m, boxes.NetVatDue);
        Assert.Equal(50m, boxes.TotalValueSalesExVAT);
        Assert.Equal(40m, boxes.TotalValuePurchasesExVAT);
    }

    [Fact]
    public async Task FlatRate_TaxesGrossTurnoverReceived_NoInputVat()
    {
        using var ctx = _db.Create();
        await SetScheme(ctx, VatScheme.FlatRate, 14.5m);

        var invId = AddSalesInvoice(ctx, 300, 60, new DateOnly(2026, 4, 5));
        AddMoney(ctx, MoneyDirection.In, 360, new DateOnly(2026, 4, 20), salesInvId: invId);
        // Purchases are irrelevant under FRS — no input VAT recovery.
        AddMoney(ctx, MoneyDirection.Out, 500, new DateOnly(2026, 5, 1));
        await ctx.SaveChangesAsync();

        var boxes = await new VatReturnService(ctx, null!).ComputeAsync(
            _businessId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(360m, boxes.TotalValueSalesExVAT);        // box 6: VAT-INCLUSIVE — the FRS quirk
        Assert.Equal(52.20m, boxes.VatDueSales);               // 360 × 14.5%
        Assert.Equal(0m, boxes.VatReclaimedCurrPeriod);        // box 4 always 0
        Assert.Equal(52.20m, boxes.NetVatDue);
        Assert.Equal(0m, boxes.TotalValuePurchasesExVAT);      // box 7: 0 under FRS
    }

    [Fact]
    public async Task StandardScheme_IsUntouched_ByTheBranching()
    {
        // InMemory provider: the accrual path aggregates server-side by design
        // (hot path over big ledgers), which SQLite cannot translate.
        var (factory, tenant) = TestDb.InMemory($"scheme-{Guid.NewGuid()}");
        using var ctx = factory();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Accrual Ltd");
        tenant.Set(b.Id, BusinessRole.Owner);
        var boxes = await new VatReturnService(ctx, null!).ComputeAsync(
            b.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
        Assert.Equal(0m, boxes.NetVatDue);
    }

    public void Dispose() => _db.Dispose();
}
