using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Quotes and purchase orders. Neither posts anything: a quote is an offer and a
/// purchase order is a commitment, and accounting recognises neither until there
/// is an invoice. Conversion is therefore the only place these meet the ledger,
/// and it produces a draft — so the figures can still be checked against what was
/// actually delivered or billed before anything is posted.
/// </summary>
public class OrderService
{
    private readonly AppDbContext _db;
    public OrderService(AppDbContext db) => _db = db;

    public async Task<SalesInvoice> ConvertQuoteAsync(Guid businessId, Guid quoteId,
        string invoiceNumber, DateOnly? invoiceDate, Guid userId)
    {
        var quote = await _db.SalesQuotes.Include(q => q.Lines).Include(q => q.Customer)
            .FirstOrDefaultAsync(q => q.Id == quoteId && q.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Quote not found.");
        if (quote.Status == QuoteStatus.Converted)
            throw new InvalidOperationException("This quote has already been invoiced.");
        if (quote.Status == QuoteStatus.Declined)
            throw new InvalidOperationException("A declined quote cannot be invoiced.");
        if (quote.Lines.Count == 0)
            throw new InvalidOperationException("The quote has no lines.");

        var date = invoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = quote.CustomerId,
            Number = invoiceNumber, Date = date,
            DueDate = date.AddDays(quote.Customer.PaymentTermsDays),
            Reference = quote.Reference,
            Notes = $"From quote {quote.Number}" + (quote.Notes is null ? "" : $"\n{quote.Notes}"),
        };
        foreach (var l in quote.Lines)
            invoice.Lines.Add(new SalesInvoiceLine
            {
                Id = Guid.NewGuid(), SalesInvoiceId = invoice.Id, ItemId = l.ItemId,
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                VatRate = l.VatRate, AccountId = l.AccountId,
            });
        DocumentPostingService.Recalculate(invoice);
        _db.SalesInvoices.Add(invoice);

        quote.Status = QuoteStatus.Converted;
        quote.ConvertedInvoiceId = invoice.Id;
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<PurchaseInvoice> ConvertOrderAsync(Guid businessId, Guid orderId,
        string invoiceNumber, DateOnly? invoiceDate, Guid userId)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Lines).Include(o => o.Vendor)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Purchase order not found.");
        if (order.Status == PurchaseOrderStatus.Converted)
            throw new InvalidOperationException("This order has already been invoiced.");
        if (order.Status == PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException("A cancelled order cannot be invoiced.");
        if (order.Lines.Count == 0)
            throw new InvalidOperationException("The order has no lines.");

        var date = invoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, VendorId = order.VendorId,
            Number = invoiceNumber, Date = date,
            DueDate = date.AddDays(order.Vendor.PaymentTermsDays),
            Reference = order.Reference,
            Notes = $"From purchase order {order.Number}" + (order.Notes is null ? "" : $"\n{order.Notes}"),
        };
        foreach (var l in order.Lines)
            invoice.Lines.Add(new PurchaseInvoiceLine
            {
                Id = Guid.NewGuid(), PurchaseInvoiceId = invoice.Id, ItemId = l.ItemId,
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                VatRate = l.VatRate, AccountId = l.AccountId,
            });
        DocumentPostingService.Recalculate(invoice);
        _db.PurchaseInvoices.Add(invoice);

        order.Status = PurchaseOrderStatus.Converted;
        order.ConvertedInvoiceId = invoice.Id;
        await _db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>Marks quotes past their expiry as expired, so the pipeline stays honest.</summary>
    public async Task<int> ExpireStaleQuotesAsync(Guid businessId, DateOnly today)
    {
        var stale = await _db.SalesQuotes
            .Where(q => q.BusinessId == businessId
                        && (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent)
                        && q.ExpiryDate < today)
            .ToListAsync();
        foreach (var q in stale) q.Status = QuoteStatus.Expired;
        if (stale.Count > 0) await _db.SaveChangesAsync();
        return stale.Count;
    }
}
