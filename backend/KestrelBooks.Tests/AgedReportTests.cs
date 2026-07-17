using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class AgedReportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private Guid _customerId;

    public AgedReportTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Aged Ltd");
        _businessId = b.Id;
        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Slow Payer Ltd" };
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        _customerId = customer.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private void AddInvoice(AppDbContext ctx, string number, DateOnly due, decimal gross, decimal paid = 0)
        => ctx.SalesInvoices.Add(new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = number, Date = due.AddDays(-30), DueDate = due,
            NetTotal = gross, GrossTotal = gross, AmountPaid = paid,
            Status = DocumentStatus.Posted,
        });

    [Fact]
    public async Task Buckets_AgeByDaysOverdue_WithPartPaymentsAndCredits()
    {
        var asOf = new DateOnly(2026, 7, 1);
        using var ctx = _db.Create();
        AddInvoice(ctx, "A", asOf.AddDays(10), 100);        // not yet due → Current
        AddInvoice(ctx, "B", asOf.AddDays(-15), 200, 50);   // 15 days overdue, £150 left → 1–30
        AddInvoice(ctx, "C", asOf.AddDays(-45), 300);       // 45 days → 31–60
        AddInvoice(ctx, "D", asOf.AddDays(-75), 400);       // 75 days → 61–90
        AddInvoice(ctx, "E", asOf.AddDays(-120), 500);      // 120 days → 90+
        AddInvoice(ctx, "F", asOf.AddDays(-200), 600, 600); // fully paid → excluded
        ctx.SalesCreditNotes.Add(new SalesCreditNote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "CN-1", Date = asOf.AddDays(-5), DueDate = asOf.AddDays(-5),
            NetTotal = 80, GrossTotal = 80, AmountPaid = 0, Status = DocumentStatus.Posted,
        }); // unapplied £80 credit, 5 days past its date → −80 in 1–30
        await ctx.SaveChangesAsync();

        var report = await new AgedReportService(ctx).AgedDebtorsAsync(_businessId, asOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(100m, row.Buckets.Current);
        Assert.Equal(70m, row.Buckets.Days30);   // 150 − 80 credit
        Assert.Equal(300m, row.Buckets.Days60);
        Assert.Equal(400m, row.Buckets.Days90);
        Assert.Equal(500m, row.Buckets.Older);
        Assert.Equal(1370m, row.Buckets.Total);
        Assert.Equal(1370m, report.Totals.Total);
    }

    [Fact]
    public async Task Statement_ListsOpenItems_WithNetTotalDue()
    {
        var asOf = new DateOnly(2026, 7, 1);
        using var ctx = _db.Create();
        AddInvoice(ctx, "INV-1", asOf.AddDays(-40), 240, 40); // £200 outstanding, 40 days overdue
        AddInvoice(ctx, "INV-2", asOf.AddDays(5), 120);       // £120 not yet due
        ctx.SalesCreditNotes.Add(new SalesCreditNote
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Number = "CN-9", Date = asOf.AddDays(-3), DueDate = asOf.AddDays(-3),
            NetTotal = 60, GrossTotal = 60, AmountPaid = 0, Status = DocumentStatus.Posted,
        });
        await ctx.SaveChangesAsync();

        var statement = await new AgedReportService(ctx).CustomerStatementAsync(_businessId, _customerId, asOf);

        Assert.Equal("Slow Payer Ltd", statement.ContactName);
        Assert.Equal(3, statement.Items.Count);
        Assert.Equal(260m, statement.TotalDue); // 200 + 120 − 60
        var overdue = statement.Items.First(i => i.Number == "INV-1");
        Assert.Equal(40, overdue.DaysOverdue);
        Assert.Equal(200m, overdue.Outstanding);
        var credit = statement.Items.First(i => i.Kind == "Credit note");
        Assert.Equal(-60m, credit.Outstanding);
    }

    public void Dispose() => _db.Dispose();
}
