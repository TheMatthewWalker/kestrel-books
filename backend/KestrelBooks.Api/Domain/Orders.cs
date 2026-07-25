namespace KestrelBooks.Api.Domain;

public enum QuoteStatus { Draft = 0, Sent = 1, Accepted = 2, Declined = 3, Converted = 4, Expired = 5 }
public enum PurchaseOrderStatus { Draft = 0, Sent = 1, Received = 2, Converted = 3, Cancelled = 4 }

/// <summary>
/// A quote is a promise, not a transaction — so it never touches the ledger.
/// Nothing is owed until the customer accepts and the work is invoiced, which is
/// exactly what conversion does: it produces a draft invoice carrying the same
/// lines, and from that point the normal posting rules take over.
/// </summary>
public class SalesQuote
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SalesQuoteLine> Lines { get; set; } = new();

    public decimal NetTotal => Lines.Sum(l => l.Net);
    public decimal VatTotal => Lines.Sum(l =>
        Math.Round(l.Net * VatRates.Percent(l.VatRate), 2, MidpointRounding.AwayFromZero));
    public decimal GrossTotal => NetTotal + VatTotal;
}

public class SalesQuoteLine : InvoiceLineBase
{
    public Guid SalesQuoteId { get; set; }
}

/// <summary>
/// A purchase order commits the business to buy but records no liability — the
/// creditor arises when the supplier invoices. Converting produces a draft
/// purchase invoice with the ordered lines, ready to be checked against what the
/// supplier actually billed.
/// </summary>
public class PurchaseOrder
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PurchaseOrderLine> Lines { get; set; } = new();

    public decimal NetTotal => Lines.Sum(l => l.Net);
    public decimal VatTotal => Lines.Sum(l =>
        Math.Round(l.Net * VatRates.Percent(l.VatRate), 2, MidpointRounding.AwayFromZero));
    public decimal GrossTotal => NetTotal + VatTotal;
}

public class PurchaseOrderLine : InvoiceLineBase
{
    public Guid PurchaseOrderId { get; set; }
}
