using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// Tracking is only useful if the dimension survives the journey from document
/// line to journal line — otherwise a department's report is missing exactly the
/// costs and sales that were coded to it. These tests follow it through both
/// invoice paths, which is where it was originally being dropped.
/// </summary>
public class TrackingTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId, _vendorId, _salesAccount, _expenseAccount;
    private Guid _north, _south;

    public TrackingTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, _, _) = TestDb.SeedBusiness(ctx, "Segmented Ltd");
        _businessId = b.Id;
        _salesAccount = sales.Id;
        _expenseAccount = ctx.Accounts.First(a => a.Type == AccountType.Expense).Id;

        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Client" };
        var vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Supplier" };
        ctx.Customers.Add(customer); ctx.Vendors.Add(vendor);

        var category = new TrackingCategory { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Region" };
        var north = new TrackingOption
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, TrackingCategoryId = category.Id, Name = "North",
        };
        var south = new TrackingOption
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, TrackingCategoryId = category.Id, Name = "South",
        };
        ctx.TrackingCategories.Add(category);
        ctx.TrackingOptions.AddRange(north, south);
        ctx.SaveChanges();

        _customerId = customer.Id; _vendorId = vendor.Id;
        _north = north.Id; _south = south.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private DocumentPostingService Docs(Api.Data.AppDbContext ctx)
    {
        var posting = new PostingService(ctx);
        return new DocumentPostingService(ctx, posting, new StockService(ctx, posting));
    }

    [Fact]
    public async Task SalesInvoice_SplitAcrossRegions_ProducesOneJournalLinePerRegion()
    {
        using var ctx = _db.Create();
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "INV-1", Date = new DateOnly(2026, 6, 1), DueDate = new DateOnly(2026, 6, 30),
        };
        // Same income account, two different regions — must not be merged.
        inv.Lines.Add(new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "North work",
            Quantity = 1, UnitPrice = 600, VatRate = VatRate.Zero,
            AccountId = _salesAccount, TrackingOptionId = _north,
        });
        inv.Lines.Add(new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "South work",
            Quantity = 1, UnitPrice = 400, VatRate = VatRate.Zero,
            AccountId = _salesAccount, TrackingOptionId = _south,
        });
        DocumentPostingService.Recalculate(inv);
        ctx.SalesInvoices.Add(inv);
        await ctx.SaveChangesAsync();

        var journal = await Docs(ctx).PostSalesInvoiceAsync(_businessId, inv.Id, _user);

        var incomeLines = journal.Lines.Where(l => l.AccountId == _salesAccount).ToList();
        Assert.Equal(2, incomeLines.Count);
        Assert.Equal(600m, incomeLines.Single(l => l.TrackingOptionId == _north).Credit);
        Assert.Equal(400m, incomeLines.Single(l => l.TrackingOptionId == _south).Credit);
        Assert.Equal(journal.Lines.Sum(l => l.Debit), journal.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PurchaseInvoice_CarriesTheDimension_SoCostsAreTrackedToo()
    {
        using var ctx = _db.Create();
        var inv = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = _vendorId,
            Number = "BILL-1", Date = new DateOnly(2026, 6, 1), DueDate = new DateOnly(2026, 6, 30),
        };
        inv.Lines.Add(new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id, Description = "North van hire",
            Quantity = 1, UnitPrice = 250, VatRate = VatRate.Zero,
            AccountId = _expenseAccount, TrackingOptionId = _north,
        });
        inv.Lines.Add(new PurchaseInvoiceLine
        {
            Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id, Description = "South van hire",
            Quantity = 1, UnitPrice = 150, VatRate = VatRate.Zero,
            AccountId = _expenseAccount, TrackingOptionId = _south,
        });
        DocumentPostingService.Recalculate(inv);
        ctx.PurchaseInvoices.Add(inv);
        await ctx.SaveChangesAsync();

        var journal = await Docs(ctx).PostPurchaseInvoiceAsync(_businessId, inv.Id, _user);

        var costLines = journal.Lines.Where(l => l.AccountId == _expenseAccount).ToList();
        Assert.Equal(2, costLines.Count);
        Assert.Equal(250m, costLines.Single(l => l.TrackingOptionId == _north).Debit);
        Assert.Equal(150m, costLines.Single(l => l.TrackingOptionId == _south).Debit);
    }

    [Fact]
    public async Task UntaggedLines_StillPostNormally_AndMergeTogether()
    {
        using var ctx = _db.Create();
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "INV-2", Date = new DateOnly(2026, 6, 1), DueDate = new DateOnly(2026, 6, 30),
        };
        foreach (var amount in new[] { 100m, 200m })
            inv.Lines.Add(new SalesInvoiceLine
            {
                Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "Untagged",
                Quantity = 1, UnitPrice = amount, VatRate = VatRate.Zero, AccountId = _salesAccount,
            });
        DocumentPostingService.Recalculate(inv);
        ctx.SalesInvoices.Add(inv);
        await ctx.SaveChangesAsync();

        var journal = await Docs(ctx).PostSalesInvoiceAsync(_businessId, inv.Id, _user);

        // No dimension means nothing to separate them by — one merged line, as before.
        var incomeLine = Assert.Single(journal.Lines.Where(l => l.AccountId == _salesAccount));
        Assert.Equal(300m, incomeLine.Credit);
        Assert.Null(incomeLine.TrackingOptionId);
    }

    public void Dispose() => _db.Dispose();
}
