namespace KestrelBooks.Api.Domain;

public enum DocumentStatus { Draft = 0, Posted = 1, Voided = 2 }

/// <summary>
/// Base for sales and purchase invoices. Drafts are freely editable;
/// posting creates the journal and locks the document.
/// </summary>
public abstract class InvoiceBase
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    public string? Reference { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public Guid? JournalEntryId { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal AmountPaid { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SalesInvoice : InvoiceBase
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<SalesInvoiceLine> Lines { get; set; } = new();
}

public class PurchaseInvoice : InvoiceBase
{
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;
    public List<PurchaseInvoiceLine> Lines { get; set; } = new();
}

public abstract class InvoiceLineBase
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public VatRate VatRate { get; set; } = VatRate.Standard20;
    /// <summary>GL account this line analyses to (income for sales, expense/asset for purchases).</summary>
    public Guid AccountId { get; set; }
    public decimal Net => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    public decimal Vat => Math.Round(Net * VatRates.Percent(VatRate), 2, MidpointRounding.AwayFromZero);
    public decimal Gross => Net + Vat;
}

public class SalesInvoiceLine : InvoiceLineBase
{
    public Guid SalesInvoiceId { get; set; }
}

public class PurchaseInvoiceLine : InvoiceLineBase
{
    public Guid PurchaseInvoiceId { get; set; }
}

public enum MoneyDirection { In = 0, Out = 1 }

/// <summary>
/// A receipt (money in) or payment (money out) through a bank account.
/// May settle an invoice (posts against the relevant control account)
/// or post directly to a chosen GL account (e.g. cash sale, rent paid).
/// </summary>
public class MoneyTransaction
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public MoneyDirection Direction { get; set; }
    public DateOnly Date { get; set; }
    public string Reference { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    /// <summary>Used when not settling an invoice: the other side of the entry.</summary>
    public Guid? DirectAccountId { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
