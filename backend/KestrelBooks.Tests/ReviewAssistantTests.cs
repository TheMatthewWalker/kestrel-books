using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The review assistant must find the things a reviewer would want, and stay
/// quiet about the things they wouldn't. The second half matters as much: a list
/// of two hundred findings gets ignored, which is worse than no list at all.
/// </summary>
public class ReviewAssistantTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private Guid _vendorId, _customerId, _expense;

    public ReviewAssistantTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Reviewed Ltd");
        _businessId = b.Id;
        _expense = ctx.Accounts.First(a => a.Type == AccountType.Expense).Id;
        var v = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Regular Supplier" };
        var c = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "A Customer" };
        ctx.Vendors.Add(v); ctx.Customers.Add(c);
        ctx.SaveChanges();
        _vendorId = v.Id; _customerId = c.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private Guid AddPurchase(Api.Data.AppDbContext ctx, string number, DateOnly date, decimal gross,
        VatRate vat = VatRate.Standard20, DocumentStatus status = DocumentStatus.Posted)
    {
        var inv = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = _vendorId,
            Number = number, Date = date, DueDate = date.AddDays(30),
            NetTotal = gross, GrossTotal = gross, Status = status,
        };
        inv.Lines.Add(new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id, Description = "Supply",
            Quantity = 1, UnitPrice = gross, VatRate = vat, AccountId = _expense,
        });
        ctx.PurchaseInvoices.Add(inv);
        ctx.SaveChanges();
        return inv.Id;
    }

    [Fact]
    public async Task TwoIdenticalBillsCloseTogether_AreFlagged()
    {
        using var ctx = _db.Create();
        AddPurchase(ctx, "INV-1", new DateOnly(2026, 6, 3), 1_200m);
        AddPurchase(ctx, "INV-2", new DateOnly(2026, 6, 10), 1_200m);

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var finding = Assert.Single(result.Findings.Where(f => f.Code == "DUP_PURCHASE"));
        Assert.Equal(FindingSeverity.Important, finding.Severity);
        Assert.Contains("1,200", finding.Detail);
    }

    [Fact]
    public async Task AMonthlyBillOfTheSameAmount_IsNotFlagged()
    {
        using var ctx = _db.Create();
        // Same supplier, same amount, but two months apart — that's a subscription.
        AddPurchase(ctx, "INV-1", new DateOnly(2026, 4, 1), 500m);
        AddPurchase(ctx, "INV-2", new DateOnly(2026, 6, 1), 500m);

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.DoesNotContain(result.Findings, f => f.Code == "DUP_PURCHASE");
    }

    [Fact]
    public async Task ASupplierBreakingItsOwnVatPattern_IsQueried()
    {
        using var ctx = _db.Create();
        // Four historical standard-rated bills establish the pattern.
        for (var i = 1; i <= 4; i++)
            AddPurchase(ctx, $"OLD-{i}", new DateOnly(2026, i, 5), 200m, VatRate.Standard20);
        // Then one zero-rated in the period under review.
        AddPurchase(ctx, "NEW-1", new DateOnly(2026, 6, 5), 200m, VatRate.Zero);

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var finding = Assert.Single(result.Findings.Where(f => f.Code == "VAT_OUTLIER"));
        Assert.Contains("zero-rated", finding.Detail);
    }

    [Fact]
    public async Task ASupplierWithNoConsistentHistory_IsNotQueried()
    {
        using var ctx = _db.Create();
        AddPurchase(ctx, "OLD-1", new DateOnly(2026, 1, 5), 200m, VatRate.Standard20);
        AddPurchase(ctx, "NEW-1", new DateOnly(2026, 6, 5), 200m, VatRate.Zero);

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.DoesNotContain(result.Findings, f => f.Code == "VAT_OUTLIER");
    }

    [Fact]
    public async Task SpendOverTheThreshold_WithNoAttachment_IsFlagged_ButSmallSpendIsNot()
    {
        using var ctx = _db.Create();
        AddPurchase(ctx, "BIG", new DateOnly(2026, 6, 5), 2_000m);
        AddPurchase(ctx, "SMALL", new DateOnly(2026, 6, 6), 40m);

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var evidence = result.Findings.Where(f => f.Code == "NO_EVIDENCE").ToList();
        Assert.Single(evidence);
        Assert.Contains("BIG", evidence[0].Detail);
        Assert.Equal(FindingSeverity.Important, evidence[0].Severity);
    }

    [Fact]
    public async Task AnOldUnpostedSalesInvoice_IsFlagged_BecauseNobodyIsChasingIt()
    {
        using var ctx = _db.Create();
        ctx.SalesInvoices.Add(new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "DRAFT-1", Date = new DateOnly(2026, 4, 1), DueDate = new DateOnly(2026, 5, 1),
            NetTotal = 900m, GrossTotal = 900m, Status = DocumentStatus.Draft,
        });
        ctx.SaveChanges();

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Single(result.Findings.Where(f => f.Code == "STALE_DRAFT"));
    }

    [Fact]
    public async Task NegativeStock_IsFlagged()
    {
        using var ctx = _db.Create();
        ctx.Items.Add(new Item
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Code = "W1", Name = "Widget",
            TrackStock = true, QuantityOnHand = -5,
        });
        ctx.SaveChanges();

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var finding = Assert.Single(result.Findings.Where(f => f.Code == "NEGATIVE_STOCK"));
        Assert.Equal(FindingSeverity.Important, finding.Severity);
    }

    [Fact]
    public async Task CleanBooks_ProduceNoFindings()
    {
        using var ctx = _db.Create();
        var id = AddPurchase(ctx, "ONE", new DateOnly(2026, 6, 5), 250m);
        ctx.Attachments.Add(new Attachment
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, EntityKind = AttachedTo.PurchaseInvoice,
            EntityId = id, FileName = "bill.pdf", StoredName = "x.pdf", ContentType = "application/pdf",
        });
        ctx.SaveChanges();

        var result = await new ReviewAssistantService(ctx)
            .ReviewAsync(_businessId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Empty(result.Findings);
        Assert.Equal(10, result.Checked);
    }

    public void Dispose() => _db.Dispose();
}
