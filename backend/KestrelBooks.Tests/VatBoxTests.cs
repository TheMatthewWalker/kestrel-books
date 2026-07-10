using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The 9-box VAT computation against a hand-worked period. Uses the InMemory
/// provider because the computation aggregates decimals server-side, which
/// SQLite cannot translate. (CI truth against PostgreSQL comes with
/// Testcontainers in a later phase.)
/// </summary>
public class VatBoxTests
{
    private readonly Func<AppDbContext> _factory;
    private readonly TenantProvider _tenant;
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();

    public VatBoxTests()
    {
        (_factory, _tenant) = TestDb.InMemory($"vat-{Guid.NewGuid()}");
        using var ctx = _factory();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "VAT Ltd");
        _businessId = b.Id;

        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "C" };
        var vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "V" };
        ctx.AddRange(customer, vendor);
        ctx.SaveChanges();
        _tenant.Set(_businessId, BusinessRole.Owner);

        // Hand-worked quarter (Apr–Jun 2026):
        //   Sales invoice: net 1,000.00, VAT 200.00
        //   Sales invoice: net 250.50,  VAT 50.10
        //   Purchase invoice: net 400.00, VAT 80.00
        // Expected: box1 250.10 · box4 80.00 · box3 250.10 · box5 170.10
        //           box6 floor(1250.50)=1250 · box7 400
        var posting = new PostingService(ctx);
        var docs = new DocumentPostingService(ctx, posting, new StockService(ctx, posting));
        var salesAcc = ctx.Accounts.First(a => a.Code == "4000").Id;
        var purchAcc = ctx.Accounts.First(a => a.Code == "5000").Id;

        void Sell(string no, decimal net, DateOnly date)
        {
            var inv = new SalesInvoice
            {
                Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = customer.Id,
                Number = no, Date = date, DueDate = date.AddDays(30),
            };
            inv.Lines.Add(new SalesInvoiceLine
            {
                Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "x",
                Quantity = 1, UnitPrice = net, VatRate = VatRate.Standard20, AccountId = salesAcc,
            });
            DocumentPostingService.Recalculate(inv);
            ctx.SalesInvoices.Add(inv);
            ctx.SaveChanges();
            docs.PostSalesInvoiceAsync(_businessId, inv.Id, _user).Wait();
        }

        Sell("S1", 1000.00m, new DateOnly(2026, 4, 10));
        Sell("S2", 250.50m, new DateOnly(2026, 5, 21));

        var pi = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = vendor.Id,
            Number = "P1", Date = new DateOnly(2026, 5, 2), DueDate = new DateOnly(2026, 6, 1),
        };
        pi.Lines.Add(new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = pi.Id, Description = "y",
            Quantity = 1, UnitPrice = 400m, VatRate = VatRate.Standard20, AccountId = purchAcc,
        });
        DocumentPostingService.Recalculate(pi);
        ctx.PurchaseInvoices.Add(pi);
        ctx.SaveChanges();
        docs.PostPurchaseInvoiceAsync(_businessId, pi.Id, _user).Wait();
    }

    [Fact]
    public async Task Boxes_MatchHandWorkedQuarter()
    {
        using var ctx = _factory();
        var vat = new VatReturnService(ctx, null!); // compute-only: HMRC client unused
        var boxes = await vat.ComputeAsync(_businessId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(250.10m, boxes.VatDueSales);            // box 1
        Assert.Equal(0m, boxes.VatDueAcquisitions);          // box 2
        Assert.Equal(250.10m, boxes.TotalVatDue);            // box 3
        Assert.Equal(80.00m, boxes.VatReclaimedCurrPeriod);  // box 4
        Assert.Equal(170.10m, boxes.NetVatDue);              // box 5
        Assert.Equal(1250m, boxes.TotalValueSalesExVAT);     // box 6 (whole pounds, floored)
        Assert.Equal(400m, boxes.TotalValuePurchasesExVAT);  // box 7
    }

    [Fact]
    public async Task InvoicesOutsidePeriod_AreExcluded()
    {
        using var ctx = _factory();
        var vat = new VatReturnService(ctx, null!);
        // A window covering only May: one sale (250.50 net / 50.10 VAT) and the purchase.
        var boxes = await vat.ComputeAsync(_businessId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Assert.Equal(50.10m, boxes.VatDueSales);
        Assert.Equal(80.00m, boxes.VatReclaimedCurrPeriod);
        Assert.Equal(250m, boxes.TotalValueSalesExVAT);
        Assert.Equal(29.90m, boxes.NetVatDue); // |50.10 − 80.00| — a repayment quarter
    }
}
