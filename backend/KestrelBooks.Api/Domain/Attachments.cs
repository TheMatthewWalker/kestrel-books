namespace KestrelBooks.Api.Domain;

public enum AttachedTo
{
    SalesInvoice = 0, PurchaseInvoice = 1, SalesCreditNote = 2, PurchaseCreditNote = 3,
    JournalEntry = 4, MoneyTransaction = 5, FixedAsset = 6, Item = 7, Customer = 8, Vendor = 9,
}

/// <summary>
/// A file pinned to a record — supplier PDF behind a purchase invoice,
/// warranty behind an asset, signed contract behind a customer. One
/// polymorphic link (kind + id) instead of ten nullable FK columns; the
/// service validates the target exists in the same business at upload.
/// Bytes live in IReceiptStorage (disk or S3), metadata here.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public AttachedTo EntityKind { get; set; }
    public Guid EntityId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
