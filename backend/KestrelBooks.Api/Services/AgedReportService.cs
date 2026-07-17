using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record AgedBuckets(decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Older)
{
    public decimal Total => Current + Days30 + Days60 + Days90 + Older;
    public static AgedBuckets operator +(AgedBuckets a, AgedBuckets b) =>
        new(a.Current + b.Current, a.Days30 + b.Days30, a.Days60 + b.Days60,
            a.Days90 + b.Days90, a.Older + b.Older);
    public static AgedBuckets Zero => new(0, 0, 0, 0, 0);
}

public record AgedContactRow(Guid ContactId, string Name, AgedBuckets Buckets);
public record AgedReport(DateOnly AsOf, List<AgedContactRow> Rows, AgedBuckets Totals);

public record StatementItem(string Kind, string Number, DateOnly Date, DateOnly DueDate,
    decimal Gross, decimal Applied, decimal Outstanding, int DaysOverdue);
public record Statement(string BusinessName, string ContactName, DateOnly AsOf,
    List<StatementItem> Items, decimal TotalDue);

/// <summary>
/// Open-item ageing. Buckets by days overdue relative to the due date:
/// Current (not yet due), 1–30, 31–60, 61–90, 90+. Unapplied credit notes
/// appear as negatives in the same buckets, so a contact's total is their
/// true net position.
///
/// Note: document inclusion respects asOf (Date ≤ asOf), but settlement state
/// (AmountPaid) is current — a backdated report shows today's outstanding
/// amounts against that day's document population. Fully historical ageing
/// needs dated allocations, which is deliberate future work.
/// </summary>
public class AgedReportService
{
    private readonly AppDbContext _db;
    public AgedReportService(AppDbContext db) => _db = db;

    private static AgedBuckets Bucket(DateOnly due, DateOnly asOf, decimal amount)
    {
        var overdue = asOf.DayNumber - due.DayNumber;
        return overdue <= 0 ? new AgedBuckets(amount, 0, 0, 0, 0)
             : overdue <= 30 ? new AgedBuckets(0, amount, 0, 0, 0)
             : overdue <= 60 ? new AgedBuckets(0, 0, amount, 0, 0)
             : overdue <= 90 ? new AgedBuckets(0, 0, 0, amount, 0)
             : new AgedBuckets(0, 0, 0, 0, amount);
    }

    public async Task<AgedReport> AgedDebtorsAsync(Guid businessId, DateOnly asOf)
    {
        var invoices = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date <= asOf && i.GrossTotal - i.AmountPaid != 0)
            .Select(i => new { i.CustomerId, ContactName = i.Customer.Name, i.DueDate,
                               Outstanding = i.GrossTotal - i.AmountPaid })
            .ToListAsync();
        var credits = await _db.SalesCreditNotes
            .Where(c => c.BusinessId == businessId && c.Status == DocumentStatus.Posted
                        && c.Date <= asOf && c.GrossTotal - c.AmountPaid != 0)
            .Select(c => new { c.CustomerId, ContactName = c.Customer.Name, c.DueDate,
                               Outstanding = -(c.GrossTotal - c.AmountPaid) })
            .ToListAsync();
        return Build(asOf, invoices.Concat(credits)
            .Select(x => (x.CustomerId, x.ContactName, x.DueDate, x.Outstanding)));
    }

    public async Task<AgedReport> AgedCreditorsAsync(Guid businessId, DateOnly asOf)
    {
        var invoices = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date <= asOf && i.GrossTotal - i.AmountPaid != 0)
            .Select(i => new { i.VendorId, ContactName = i.Vendor.Name, i.DueDate,
                               Outstanding = i.GrossTotal - i.AmountPaid })
            .ToListAsync();
        var credits = await _db.PurchaseCreditNotes
            .Where(c => c.BusinessId == businessId && c.Status == DocumentStatus.Posted
                        && c.Date <= asOf && c.GrossTotal - c.AmountPaid != 0)
            .Select(c => new { c.VendorId, ContactName = c.Vendor.Name, c.DueDate,
                               Outstanding = -(c.GrossTotal - c.AmountPaid) })
            .ToListAsync();
        return Build(asOf, invoices.Concat(credits)
            .Select(x => (x.VendorId, x.ContactName, x.DueDate, x.Outstanding)));
    }

    private static AgedReport Build(DateOnly asOf,
        IEnumerable<(Guid contactId, string name, DateOnly due, decimal outstanding)> items)
    {
        var rows = items
            .GroupBy(x => new { x.contactId, x.name })
            .Select(g => new AgedContactRow(g.Key.contactId, g.Key.name,
                g.Aggregate(AgedBuckets.Zero, (acc, x) => acc + Bucket(x.due, asOf, x.outstanding))))
            .Where(r => r.Buckets.Total != 0 || r.Buckets.Current != 0)
            .OrderByDescending(r => r.Buckets.Total)
            .ToList();
        return new AgedReport(asOf, rows,
            rows.Aggregate(AgedBuckets.Zero, (acc, r) => acc + r.Buckets));
    }

    /// <summary>Open-item customer statement — the chase document. PDF rendering arrives with 4.6.</summary>
    public async Task<Statement> CustomerStatementAsync(Guid businessId, Guid customerId, DateOnly asOf)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        var customer = await _db.Customers.FirstAsync(c => c.Id == customerId && c.BusinessId == businessId);

        var items = new List<StatementItem>();
        var invoices = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.CustomerId == customerId
                        && i.Status == DocumentStatus.Posted && i.Date <= asOf
                        && i.GrossTotal - i.AmountPaid != 0)
            .ToListAsync();
        items.AddRange(invoices.Select(i => new StatementItem("Invoice", i.Number, i.Date, i.DueDate,
            i.GrossTotal, i.AmountPaid, i.GrossTotal - i.AmountPaid,
            Math.Max(0, asOf.DayNumber - i.DueDate.DayNumber))));

        var credits = await _db.SalesCreditNotes
            .Where(c => c.BusinessId == businessId && c.CustomerId == customerId
                        && c.Status == DocumentStatus.Posted && c.Date <= asOf
                        && c.GrossTotal - c.AmountPaid != 0)
            .ToListAsync();
        items.AddRange(credits.Select(c => new StatementItem("Credit note", c.Number, c.Date, c.DueDate,
            -c.GrossTotal, -c.AmountPaid, -(c.GrossTotal - c.AmountPaid), 0)));

        var ordered = items.OrderBy(i => i.Date).ThenBy(i => i.Number).ToList();
        return new Statement(business.Name, customer.Name, asOf, ordered,
            ordered.Sum(i => i.Outstanding));
    }
}
