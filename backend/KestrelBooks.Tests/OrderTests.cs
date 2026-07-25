using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The accounting point: a quote and a purchase order are not transactions, so
/// nothing may reach the ledger until conversion — and conversion produces a
/// draft, not a posted document.
/// </summary>
public class OrderTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId, _vendorId, _salesAccount, _expenseAccount;

    public OrderTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, _, _) = TestDb.SeedBusiness(ctx, "Pipeline Ltd");
        _businessId = b.Id;
        _salesAccount = sales.Id;
        _expenseAccount = ctx.Accounts.First(a => a.Type == AccountType.Expense).Id;
        var c = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Prospect Ltd", PaymentTermsDays = 14 };
        var v = new Vendor { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Supplier Ltd", PaymentTermsDays = 30 };
        ctx.Customers.Add(c); ctx.Vendors.Add(v);
        ctx.SaveChanges();
        _customerId = c.Id; _vendorId = v.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private Guid AddQuote(Api.Data.AppDbContext ctx, QuoteStatus status = QuoteStatus.Sent,
        DateOnly? expiry = null)
    {
        var q = new SalesQuote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "QU-001", Date = new DateOnly(2026, 6, 1),
            ExpiryDate = expiry ?? new DateOnly(2026, 7, 1), Status = status,
        };
        q.Lines.Add(new SalesQuoteLine
        {
            Id = Guid.NewGuid(), SalesQuoteId = q.Id, Description = "Consultancy",
            Quantity = 2, UnitPrice = 500, VatRate = VatRate.Standard20, AccountId = _salesAccount,
        });
        ctx.SalesQuotes.Add(q);
        ctx.SaveChanges();
        return q.Id;
    }

    [Fact]
    public async Task Quote_PostsNothing_UntilItIsConverted()
    {
        using var ctx = _db.Create();
        AddQuote(ctx);

        // A quote is an offer, not a transaction.
        Assert.Empty(await ctx.Journals.ToListAsync());
        Assert.Empty(await ctx.SalesInvoices.ToListAsync());
    }

    [Fact]
    public async Task Converting_CarriesTheLinesAcross_AndProducesADraft()
    {
        using var ctx = _db.Create();
        var quoteId = AddQuote(ctx);

        var invoice = await new OrderService(ctx).ConvertQuoteAsync(_businessId, quoteId,
            "INV-500", new DateOnly(2026, 6, 20), _user);

        Assert.Equal(DocumentStatus.Draft, invoice.Status);   // still needs a human to post it
        Assert.Equal(1_200m, invoice.GrossTotal);             // 1,000 net + 20% VAT
        Assert.Equal(new DateOnly(2026, 7, 4), invoice.DueDate); // 14-day terms from the customer
        Assert.Single(invoice.Lines);
        Assert.Contains("QU-001", invoice.Notes);

        var quote = await ctx.SalesQuotes.FirstAsync(q => q.Id == quoteId);
        Assert.Equal(QuoteStatus.Converted, quote.Status);
        Assert.Equal(invoice.Id, quote.ConvertedInvoiceId);

        // Still nothing in the ledger — posting the invoice is a separate decision.
        Assert.Empty(await ctx.Journals.ToListAsync());
    }

    [Fact]
    public async Task AQuote_CannotBeInvoicedTwice_OrAfterBeingDeclined()
    {
        using var ctx = _db.Create();
        var svc = new OrderService(ctx);
        var quoteId = AddQuote(ctx);
        await svc.ConvertQuoteAsync(_businessId, quoteId, "INV-1", null, _user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConvertQuoteAsync(_businessId, quoteId, "INV-2", null, _user));

        var declined = AddQuote(ctx, QuoteStatus.Declined);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConvertQuoteAsync(_businessId, declined, "INV-3", null, _user));
    }

    [Fact]
    public async Task StaleQuotes_Expire_ButAcceptedAndConvertedOnesAreLeftAlone()
    {
        using var ctx = _db.Create();
        AddQuote(ctx, QuoteStatus.Sent, expiry: new DateOnly(2026, 5, 1));      // stale
        AddQuote(ctx, QuoteStatus.Accepted, expiry: new DateOnly(2026, 5, 1));  // accepted, leave it
        AddQuote(ctx, QuoteStatus.Sent, expiry: new DateOnly(2026, 12, 1));     // still live

        var expired = await new OrderService(ctx)
            .ExpireStaleQuotesAsync(_businessId, new DateOnly(2026, 6, 15));

        Assert.Equal(1, expired);
        Assert.Equal(1, await ctx.SalesQuotes.CountAsync(q => q.Status == QuoteStatus.Expired));
        Assert.Equal(1, await ctx.SalesQuotes.CountAsync(q => q.Status == QuoteStatus.Accepted));
    }

    [Fact]
    public async Task PurchaseOrder_ConvertsToADraftPurchaseInvoice_WithSupplierTerms()
    {
        using var ctx = _db.Create();
        var order = new PurchaseOrder
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = _vendorId,
            Number = "PO-100", Date = new DateOnly(2026, 6, 1),
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            Id = Guid.NewGuid(), PurchaseOrderId = order.Id, Description = "Steel",
            Quantity = 10, UnitPrice = 25, VatRate = VatRate.Standard20, AccountId = _expenseAccount,
        });
        ctx.PurchaseOrders.Add(order);
        ctx.SaveChanges();

        var invoice = await new OrderService(ctx).ConvertOrderAsync(_businessId, order.Id,
            "SUPP-77", new DateOnly(2026, 6, 10), _user);

        Assert.Equal(DocumentStatus.Draft, invoice.Status);
        Assert.Equal(300m, invoice.GrossTotal);                  // 250 + 20%
        Assert.Equal(new DateOnly(2026, 7, 10), invoice.DueDate); // 30-day supplier terms
        Assert.Contains("PO-100", invoice.Notes);
        Assert.Empty(await ctx.Journals.ToListAsync());
    }

    [Fact]
    public async Task EmptyDocuments_CannotBeConverted()
    {
        using var ctx = _db.Create();
        var empty = new SalesQuote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "QU-EMPTY", Date = new DateOnly(2026, 6, 1), ExpiryDate = new DateOnly(2026, 7, 1),
        };
        ctx.SalesQuotes.Add(empty);
        ctx.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderService(ctx).ConvertQuoteAsync(_businessId, empty.Id, "INV-X", null, _user));
    }

    public void Dispose() => _db.Dispose();
}
