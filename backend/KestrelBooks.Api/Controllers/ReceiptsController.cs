using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record ConfirmScanRequest(
    string VendorName, DateOnly Date, decimal Net, decimal Vat,
    Guid ExpenseAccountId,
    // "invoice" creates a draft purchase invoice (pay later, via Trade Creditors);
    // "money" posts an immediate money-out from the given bank account (paid on the spot).
    string Mode, Guid? BankAccountId);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/receipts")]
public class ReceiptsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly IReceiptExtractor _extractor;
    private readonly IReceiptStorage _storage;
    private readonly DocumentPostingService _docs;
    private readonly PostingService _posting;
    public ReceiptsController(AppDbContext db, AccessService access, IReceiptExtractor extractor,
        IReceiptStorage storage, DocumentPostingService docs, PostingService posting)
    {
        _db = db; _access = access; _extractor = extractor; _storage = storage; _docs = docs; _posting = posting;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);
        var query = _db.ReceiptScans.Where(r => r.BusinessId == businessId);
        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        return Ok(await query.OrderByDescending(r => r.UploadedAtUtc).Skip(skip).Take(take).ToListAsync());
    }

    /// <summary>Upload a receipt photo; stores the image and runs extraction.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Upload(Guid businessId, IFormFile file)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No image received." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var contentType = string.IsNullOrEmpty(file.ContentType) ? "image/jpeg" : file.ContentType;
        var ext = contentType.Contains("png") ? ".png" : ".jpg";

        var scan = new ReceiptScan
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            OriginalFileName = file.FileName,
            ContentType = contentType,
            StoredFileName = await _storage.SaveAsync(businessId, ext, bytes),
        };

        var extracted = await _extractor.ExtractAsync(bytes, contentType);
        scan.VendorName = extracted.Vendor;
        scan.ReceiptDate = extracted.Date;
        scan.NetAmount = extracted.Net;
        scan.VatAmount = extracted.Vat;
        scan.GrossAmount = extracted.Gross;
        scan.ExtractionNotes = extracted.Notes;
        scan.Status = ScanStatus.Extracted;

        _db.ReceiptScans.Add(scan);
        await _db.SaveChangesAsync();
        return Ok(scan);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> Image(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var scan = await _db.ReceiptScans.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == businessId);
        if (scan is null) return NotFound();
        var bytes = await _storage.LoadAsync(businessId, scan.StoredFileName);
        return bytes is null ? NotFound() : File(bytes, scan.ContentType);
    }

    /// <summary>
    /// Confirm the (possibly corrected) fields. Creates either a draft purchase
    /// invoice from an auto-created vendor, or an immediately posted money-out.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid businessId, Guid id, ConfirmScanRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var scan = await _db.ReceiptScans.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == businessId);
        if (scan is null) return NotFound();
        if (scan.Status == ScanStatus.Confirmed)
            return BadRequest(new { error = "This receipt has already been confirmed." });

        scan.VendorName = req.VendorName;
        scan.ReceiptDate = req.Date;
        scan.NetAmount = req.Net;
        scan.VatAmount = req.Vat;
        scan.GrossAmount = req.Net + req.Vat;

        if (req.Mode == "money")
        {
            if (req.BankAccountId is null)
                return BadRequest(new { error = "Choose the bank account the receipt was paid from." });
            // Paid on the spot: no creditor arises. VAT still needs splitting out,
            // so post gross Cr Bank / net Dr expense / VAT Dr Input VAT via a manual-source journal
            // wrapped in a money transaction for the audit trail.
            var tx = new MoneyTransaction
            {
                Id = Guid.NewGuid(), BusinessId = businessId, Direction = MoneyDirection.Out,
                Date = req.Date, Reference = $"Receipt — {req.VendorName}",
                Amount = req.Net + req.Vat, BankAccountId = req.BankAccountId.Value,
                DirectAccountId = req.ExpenseAccountId, Notes = "From receipt scan",
            };
            _db.MoneyTransactions.Add(tx);
            await _db.SaveChangesAsync();

            if (req.Vat > 0)
            {
                // Post the VAT split as its own balanced journal:
                // the money-out posts gross to the expense account; this moves the VAT
                // element from the expense account into Input VAT.
                var inputVat = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.VatInput);
                await _docs.PostMoneyTransactionAsync(businessId, tx.Id, AccessService.UserId(User));
                var vatJournal = await _posting.CreateDraftAsync(businessId, AccessService.UserId(User),
                    req.Date, $"VAT-{scan.Id.ToString()[..8]}", $"Input VAT on receipt — {req.VendorName}",
                    SourceType.Manual, scan.Id,
                    new[]
                    {
                        new DraftLine(inputVat.Id, req.Vat, 0, "Input VAT reclaim"),
                        new DraftLine(req.ExpenseAccountId, 0, req.Vat, "VAT element out of expense"),
                    });
                await _posting.PostAsync(businessId, vatJournal.Id, AccessService.UserId(User));
            }
            else
            {
                await _docs.PostMoneyTransactionAsync(businessId, tx.Id, AccessService.UserId(User));
            }
            scan.MoneyTransactionId = tx.Id;
        }
        else
        {
            // Pay later: draft purchase invoice through the purchase ledger.
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v =>
                v.BusinessId == businessId && v.Name.ToLower() == req.VendorName.ToLower());
            if (vendor is null)
            {
                vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = businessId, Name = req.VendorName };
                _db.Vendors.Add(vendor);
            }
            var vatRate = req.Net > 0 && Math.Abs(req.Vat / req.Net - 0.20m) < 0.01m ? VatRate.Standard20
                        : req.Vat == 0 ? VatRate.Zero
                        : req.Net > 0 && Math.Abs(req.Vat / req.Net - 0.05m) < 0.01m ? VatRate.Reduced5
                        : VatRate.Standard20;
            var inv = new PurchaseInvoice
            {
                Id = Guid.NewGuid(), BusinessId = businessId, VendorId = vendor.Id,
                Number = $"RCPT-{scan.Id.ToString()[..8].ToUpper()}",
                Date = req.Date, DueDate = req.Date.AddDays(vendor.PaymentTermsDays),
                Notes = "Created from receipt scan",
            };
            inv.Lines.Add(new PurchaseInvoiceLine
            {
                Id = Guid.NewGuid(), PurchaseInvoiceId = inv.Id,
                Description = $"Receipt — {req.VendorName}", Quantity = 1,
                UnitPrice = req.Net, VatRate = vatRate, AccountId = req.ExpenseAccountId,
            });
            DocumentPostingService.Recalculate(inv);
            _db.PurchaseInvoices.Add(inv);
            scan.PurchaseInvoiceId = inv.Id;
        }

        scan.Status = ScanStatus.Confirmed;
        await _db.SaveChangesAsync();
        return Ok(new { scan.Id, scan.PurchaseInvoiceId, scan.MoneyTransactionId });
    }
}
