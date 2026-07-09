namespace KestrelBooks.Api.Domain;

public enum ScanStatus { Uploaded = 0, Extracted = 1, Confirmed = 2, Discarded = 3 }

/// <summary>
/// A photographed purchase receipt. The image is stored on disk; an extractor
/// (Claude vision if an API key is configured, otherwise manual entry) proposes
/// vendor/date/amounts, and confirming creates a draft purchase invoice or a
/// posted money-out transaction.
/// </summary>
public class ReceiptScan
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public ScanStatus Status { get; set; } = ScanStatus.Uploaded;

    // Extracted / user-confirmed fields
    public string? VendorName { get; set; }
    public DateOnly? ReceiptDate { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? ExtractionNotes { get; set; }    // extractor provenance / raw hints

    // What the confirmation produced
    public Guid? PurchaseInvoiceId { get; set; }
    public Guid? MoneyTransactionId { get; set; }
}
