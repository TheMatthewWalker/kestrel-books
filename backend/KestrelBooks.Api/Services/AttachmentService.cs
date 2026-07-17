using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public class AttachmentService
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/heic"] = ".heic",
        ["text/csv"] = ".csv",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["text/plain"] = ".txt",
    };

    private readonly AppDbContext _db;
    private readonly IReceiptStorage _storage;
    public AttachmentService(AppDbContext db, IReceiptStorage storage)
    {
        _db = db; _storage = storage;
    }

    public async Task<Attachment> SaveAsync(Guid businessId, AttachedTo kind, Guid entityId,
        string fileName, string contentType, byte[] data, Guid userId)
    {
        if (data.Length == 0) throw new InvalidOperationException("The file is empty.");
        if (data.Length > MaxBytes) throw new InvalidOperationException("File exceeds the 10 MB limit.");
        if (!AllowedTypes.TryGetValue(contentType, out var extension))
            throw new InvalidOperationException(
                "Unsupported file type. Allowed: PDF, JPG, PNG, HEIC, CSV, XLSX, DOCX, TXT.");
        if (!await EntityExistsAsync(businessId, kind, entityId))
            throw new KeyNotFoundException("The record this file should attach to was not found.");

        var storedName = await _storage.SaveAsync(businessId, extension, data);
        var attachment = new Attachment
        {
            Id = Guid.NewGuid(), BusinessId = businessId, EntityKind = kind, EntityId = entityId,
            FileName = Path.GetFileName(fileName), StoredName = storedName,
            ContentType = contentType, SizeBytes = data.Length, UploadedBy = userId,
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync();
        return attachment;
    }

    public Task<List<Attachment>> ListAsync(Guid businessId, AttachedTo kind, Guid entityId) =>
        _db.Attachments
            .Where(a => a.BusinessId == businessId && a.EntityKind == kind && a.EntityId == entityId)
            .OrderByDescending(a => a.UploadedAtUtc)
            .ToListAsync();

    public async Task<(Attachment meta, byte[] data)?> GetAsync(Guid businessId, Guid id)
    {
        var meta = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (meta is null) return null;
        var data = await _storage.LoadAsync(businessId, meta.StoredName);
        return data is null ? null : (meta, data);
    }

    public async Task<bool> DeleteAsync(Guid businessId, Guid id)
    {
        var meta = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (meta is null) return false;
        await _storage.DeleteAsync(businessId, meta.StoredName);
        _db.Attachments.Remove(meta);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>The polymorphic link's integrity check: the target must exist in this business.</summary>
    private Task<bool> EntityExistsAsync(Guid businessId, AttachedTo kind, Guid id) => kind switch
    {
        AttachedTo.SalesInvoice => _db.SalesInvoices.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.PurchaseInvoice => _db.PurchaseInvoices.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.SalesCreditNote => _db.SalesCreditNotes.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.PurchaseCreditNote => _db.PurchaseCreditNotes.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.JournalEntry => _db.Journals.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.MoneyTransaction => _db.MoneyTransactions.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.FixedAsset => _db.FixedAssets.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.Item => _db.Items.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.Customer => _db.Customers.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        AttachedTo.Vendor => _db.Vendors.AnyAsync(x => x.Id == id && x.BusinessId == businessId),
        _ => Task.FromResult(false),
    };
}
