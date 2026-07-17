using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class PracticeDashboardTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _userId = Guid.NewGuid();

    public PracticeDashboardTests() { }

    private Guid SeedClient(string name, int yearStartMonth, string? vrn, VatScheme scheme = VatScheme.StandardAccrual)
    {
        using var ctx = _db.Create();
        var b = new Business { Id = Guid.NewGuid(), Name = name, YearStartMonth = yearStartMonth, VatScheme = scheme };
        ctx.Businesses.Add(b);
        ctx.Accounts.AddRange(CoaSeeder.DefaultChart(b.Id));
        ctx.UserBusinessAccess.Add(new UserBusinessAccess { UserId = _userId, BusinessId = b.Id, Role = BusinessRole.Owner });
        if (vrn != null)
            ctx.HmrcConnections.Add(new HmrcConnection { Id = Guid.NewGuid(), BusinessId = b.Id, Vrn = vrn });
        ctx.SaveChanges();
        return b.Id;
    }

    private void AddOverdueInvoice(Guid businessId, decimal gross, DateOnly due)
    {
        using var ctx = _db.Create();
        var cust = new Customer { Id = Guid.NewGuid(), BusinessId = businessId, Name = "Debtor" };
        ctx.Customers.Add(cust);
        ctx.SalesInvoices.Add(new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = cust.Id,
            Number = "INV-1", Date = due.AddDays(-30), DueDate = due,
            NetTotal = gross, GrossTotal = gross, AmountPaid = 0, Status = DocumentStatus.Posted,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Dashboard_SpansAllAccessibleClients_WithTotals()
    {
        var a = SeedClient("Client A", 4, "111111111");   // April year start → 31 Mar year end; VAT registered
        var b = SeedClient("Client B", 1, null);          // Dec year end; not VAT registered
        AddOverdueInvoice(a, 1200, new DateOnly(2026, 3, 1)); // very overdue by a July "today"
        AddOverdueInvoice(b, 300, new DateOnly(2026, 7, 10)); // recently due

        using var ctx = _db.Create();
        var summary = await new PracticeDashboardService(ctx).BuildAsync(_userId, new DateOnly(2026, 7, 17), 400);

        Assert.Equal(2, summary.ClientCount);
        Assert.Equal(1500m, summary.TotalReceivables);       // 1200 + 300
        Assert.Equal(1500m, summary.TotalOverdue);           // both past due by 17 Jul
        // Only Client A is VAT-registered, so exactly one VAT return deadline.
        Assert.Equal(1, summary.Deadlines.Count(d => d.Kind == DeadlineKind.VatReturn));
        // Both clients have a year-end deadline within the wide horizon.
        Assert.Equal(2, summary.Deadlines.Count(d => d.Kind == DeadlineKind.YearEnd));
        // Client A's invoice is 90+ days overdue → a chase deadline; B's is not.
        Assert.Equal(1, summary.Deadlines.Count(d => d.Kind == DeadlineKind.OverdueReceivables));
    }

    [Fact]
    public async Task Dashboard_ExcludesClientsTheUserCannotAccess()
    {
        SeedClient("Mine", 4, "111111111");
        // A business owned by someone else entirely.
        using (var ctx = _db.Create())
        {
            var other = new Business { Id = Guid.NewGuid(), Name = "Not mine", YearStartMonth = 4 };
            ctx.Businesses.Add(other);
            ctx.Accounts.AddRange(CoaSeeder.DefaultChart(other.Id));
            ctx.UserBusinessAccess.Add(new UserBusinessAccess
            {
                UserId = Guid.NewGuid(), BusinessId = other.Id, Role = BusinessRole.Owner
            });
            ctx.SaveChanges();
        }

        using var ctx2 = _db.Create();
        var summary = await new PracticeDashboardService(ctx2).BuildAsync(_userId, new DateOnly(2026, 7, 17), 400);
        Assert.Equal(1, summary.ClientCount);
        Assert.All(summary.Deadlines, d => Assert.NotEqual("Not mine", d.BusinessName));
    }

    [Fact]
    public async Task VatReturn_MarkedActioned_WhenAlreadySubmitted()
    {
        var a = SeedClient("Filed Ltd", 4, "222222222");
        var qEnd = new DateOnly(2026, 9, 30); // quarter end after a July today
        using (var ctx = _db.Create())
        {
            ctx.VatSubmissions.Add(new VatSubmission
            {
                Id = Guid.NewGuid(), BusinessId = a, PeriodKey = "26C3",
                PeriodFrom = new DateOnly(2026, 7, 1), PeriodTo = qEnd,
                BoxesJson = "{}", SubmittedBy = _userId,
            });
            ctx.SaveChanges();
        }

        using var ctx2 = _db.Create();
        var summary = await new PracticeDashboardService(ctx2).BuildAsync(_userId, new DateOnly(2026, 8, 1), 120);
        var vat = summary.Deadlines.Single(d => d.Kind == DeadlineKind.VatReturn);
        Assert.True(vat.Actioned);
        Assert.Equal(0, summary.VatReturnsDueSoon); // actioned ones don't count as "due soon"
    }

    public void Dispose() => _db.Dispose();
}
