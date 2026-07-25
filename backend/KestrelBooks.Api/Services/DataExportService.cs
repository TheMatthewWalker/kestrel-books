using System.Globalization;
using System.IO.Compression;
using System.Text;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// A complete, portable copy of one client's books as a zip of CSVs.
///
/// This exists because a practice cannot responsibly hold books it cannot hand
/// back. A client leaves, the practice is sold, or the software is retired — in
/// every case the answer has to be "here is everything, in a format anything can
/// read". CSV rather than a proprietary dump for exactly that reason: it opens in
/// Excel, imports into any other package, and will still be readable in ten years.
/// </summary>
public class DataExportService
{
    private readonly AppDbContext _db;
    public DataExportService(AppDbContext db) => _db = db;

    public async Task<byte[]> ExportAsync(Guid businessId)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddCsv(zip, "accounts.csv",
                new[] { "Code", "Name", "Type", "SubType", "IsBank", "SystemTag" },
                (await _db.Accounts.Where(a => a.BusinessId == businessId)
                    .OrderBy(a => a.Code).ToListAsync())
                    .Select(a => new[] { a.Code, a.Name, a.Type.ToString(), a.SubType ?? "",
                        a.IsBank.ToString(), a.SystemTag ?? "" }));

            // The ledger: the one file that must be complete, because everything
            // else in the books is derived from it.
            await AddCsv(zip, "journal-lines.csv",
                new[] { "JournalNumber", "Date", "Status", "Source", "Reference", "Narrative",
                        "AccountCode", "AccountName", "Debit", "Credit", "LineDescription" },
                (await _db.JournalLines
                    .Where(l => l.JournalEntry.BusinessId == businessId)
                    .Select(l => new
                    {
                        l.JournalEntry.Number, l.JournalEntry.Date, l.JournalEntry.Status,
                        l.JournalEntry.Source, l.JournalEntry.Reference, l.JournalEntry.Narrative,
                        l.Account.Code, AccountName = l.Account.Name, l.Debit, l.Credit, l.Description,
                    })
                    .OrderBy(l => l.Number).ToListAsync())
                    .Select(l => new[] { l.Number.ToString(), Iso(l.Date), l.Status.ToString(),
                        l.Source.ToString(), l.Reference, l.Narrative, l.Code, l.AccountName,
                        Money(l.Debit), Money(l.Credit), l.Description ?? "" }));

            await AddCsv(zip, "customers.csv",
                new[] { "Name", "Email", "Phone", "Address1", "Address2", "City", "Postcode", "VatNumber", "TermsDays" },
                (await _db.Customers.Where(c => c.BusinessId == businessId).OrderBy(c => c.Name).ToListAsync())
                    .Select(c => new[] { c.Name, c.Email ?? "", c.Phone ?? "", c.AddressLine1 ?? "",
                        c.AddressLine2 ?? "", c.City ?? "", c.Postcode ?? "", c.VatNumber ?? "",
                        c.PaymentTermsDays.ToString() }));

            await AddCsv(zip, "suppliers.csv",
                new[] { "Name", "Email", "Phone", "Address1", "Address2", "City", "Postcode", "VatNumber", "TermsDays" },
                (await _db.Vendors.Where(v => v.BusinessId == businessId).OrderBy(v => v.Name).ToListAsync())
                    .Select(v => new[] { v.Name, v.Email ?? "", v.Phone ?? "", v.AddressLine1 ?? "",
                        v.AddressLine2 ?? "", v.City ?? "", v.Postcode ?? "", v.VatNumber ?? "",
                        v.PaymentTermsDays.ToString() }));

            await AddCsv(zip, "sales-invoices.csv",
                new[] { "Number", "Customer", "Date", "DueDate", "Status", "Net", "Vat", "Gross", "Paid" },
                (await _db.SalesInvoices.Where(i => i.BusinessId == businessId)
                    .Select(i => new { i.Number, Contact = i.Customer.Name, i.Date, i.DueDate,
                        i.Status, i.NetTotal, i.VatTotal, i.GrossTotal, i.AmountPaid })
                    .OrderBy(i => i.Number).ToListAsync())
                    .Select(i => new[] { i.Number, i.Contact, Iso(i.Date), Iso(i.DueDate),
                        i.Status.ToString(), Money(i.NetTotal), Money(i.VatTotal),
                        Money(i.GrossTotal), Money(i.AmountPaid) }));

            await AddCsv(zip, "purchase-invoices.csv",
                new[] { "Number", "Supplier", "Date", "DueDate", "Status", "Net", "Vat", "Gross", "Paid" },
                (await _db.PurchaseInvoices.Where(i => i.BusinessId == businessId)
                    .Select(i => new { i.Number, Contact = i.Vendor.Name, i.Date, i.DueDate,
                        i.Status, i.NetTotal, i.VatTotal, i.GrossTotal, i.AmountPaid })
                    .OrderBy(i => i.Number).ToListAsync())
                    .Select(i => new[] { i.Number, i.Contact, Iso(i.Date), Iso(i.DueDate),
                        i.Status.ToString(), Money(i.NetTotal), Money(i.VatTotal),
                        Money(i.GrossTotal), Money(i.AmountPaid) }));

            await AddCsv(zip, "items.csv",
                new[] { "Code", "Name", "Kind", "SalesPrice", "PurchasePrice", "TrackStock", "QuantityOnHand", "AvgUnitCost" },
                (await _db.Items.Where(i => i.BusinessId == businessId).OrderBy(i => i.Code).ToListAsync())
                    .Select(i => new[] { i.Code, i.Name, i.Kind.ToString(), Money(i.SalesPrice),
                        Money(i.PurchasePrice), i.TrackStock.ToString(),
                        i.QuantityOnHand.ToString(CultureInfo.InvariantCulture), Money(i.AvgUnitCost) }));

            await AddCsv(zip, "fixed-assets.csv",
                new[] { "Code", "Description", "Category", "Status", "Acquired", "Cost",
                        "Residual", "Method", "AccumulatedDepreciation", "NetBookValue", "DisposalDate", "DisposalGainLoss" },
                (await _db.FixedAssets.Where(a => a.BusinessId == businessId).OrderBy(a => a.Code).ToListAsync())
                    .Select(a => new[] { a.Code, a.Description, a.Category ?? "", a.Status.ToString(),
                        Iso(a.AcquisitionDate), Money(a.Cost), Money(a.ResidualValue), a.Method.ToString(),
                        Money(a.AccumulatedDepreciation), Money(a.Cost - a.AccumulatedDepreciation),
                        a.DisposalDate is null ? "" : Iso(a.DisposalDate.Value), Money(a.DisposalGainLoss) }));

            await AddCsv(zip, "vat-submissions.csv",
                new[] { "PeriodFrom", "PeriodTo", "PeriodKey", "SubmittedUtc", "FormBundle", "Boxes" },
                (await _db.VatSubmissions.Where(v => v.BusinessId == businessId)
                    .OrderBy(v => v.PeriodTo).ToListAsync())
                    .Select(v => new[] { Iso(v.PeriodFrom), Iso(v.PeriodTo), v.PeriodKey,
                        v.SubmittedAtUtc.ToString("O"), v.FormBundleNumber ?? "", v.BoxesJson }));

            await AddCsv(zip, "audit-trail.csv",
                new[] { "When", "User", "EntityType", "EntityId", "Action", "Changes" },
                (await _db.AuditEntries.Where(a => a.BusinessId == businessId)
                    .OrderBy(a => a.AtUtc).ToListAsync())
                    .Select(a => new[] { a.AtUtc.ToString("O"), a.UserName ?? a.UserId.ToString(),
                        a.EntityType, a.EntityId.ToString(), a.Action.ToString(), a.Changes }));

            // A plain-English note so whoever opens this in five years knows what it is.
            var readme = zip.CreateEntry("README.txt");
            await using var writer = new StreamWriter(readme.Open(), Encoding.UTF8);
            await writer.WriteAsync($"""
                KestrelBooks export — {business.Name}
                Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC

                This is a complete copy of the accounting records for this business,
                as comma-separated values with a header row in each file.

                journal-lines.csv is the ledger and is the authoritative record: every
                other file in this export is either reference data or something derived
                from those journal lines. If you are migrating to another system, that
                is the file to load, and the trial balance from it should agree to the
                trial balance printed from KestrelBooks on the same date.

                Amounts are in {business.BaseCurrency}, formatted with a decimal point and
                no thousands separator. Dates are ISO 8601 (YYYY-MM-DD).

                Attachments and receipt images are not included in this file; export
                those separately from the attachments area if they are needed.
                """);
        }
        return ms.ToArray();
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static async Task AddCsv(ZipArchive zip, string name,
        IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            await writer.WriteLineAsync(string.Join(",", row.Select(Escape)));
    }

    /// <summary>RFC 4180 quoting — doubled quotes, and always quoted so Excel
    /// cannot reinterpret a reference like 0012 as a number.</summary>
    private static string Escape(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
