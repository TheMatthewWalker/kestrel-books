using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

public class RecurringInvoiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _customerId, _salesAcc;

    public RecurringInvoiceTests()
    {
        using var ctx = _db.Create();
        var (b, _, sales, _, _) = TestDb.SeedBusiness(ctx, "Recur Ltd");
        _businessId = b.Id; _salesAcc = sales.Id;
        var customer = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Retainer Client" };
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        _customerId = customer.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private RecurringInvoiceService Svc(AppDbContext ctx) =>
        new(ctx, new DocumentPostingService(ctx, new PostingService(ctx),
            new StockService(ctx, new PostingService(ctx))), _db.Tenant);

    private Guid AddTemplate(AppDbContext ctx, RecurrenceFrequency freq, DateOnly firstRun,
        bool autoPost = false, DateOnly? end = null)
    {
        var t = new RecurringInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _customerId,
            Name = "Monthly retainer", NumberPrefix = "RET", NextNumber = 1,
            Frequency = freq, PaymentTermsDays = 14, NextRunDate = firstRun,
            EndDate = end, AutoPost = autoPost,
        };
        t.Lines.Add(new RecurringInvoiceLine
        {
            Id = Guid.NewGuid(), RecurringInvoiceId = t.Id, Description = "Retainer",
            Quantity = 1, UnitPrice = 500, VatRate = VatRate.Standard20, AccountId = _salesAcc,
        });
        ctx.RecurringInvoices.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    [Fact]
    public async Task RunNow_GeneratesDraft_WithComputedDatesAndNumber()
    {
        using var ctx = _db.Create();
        var id = AddTemplate(ctx, RecurrenceFrequency.Monthly, new DateOnly(2026, 6, 1));

        var created = await Svc(ctx).RunTemplateAsync(_businessId, id, new DateOnly(2026, 6, 15), _user);

        var invoiceId = Assert.Single(created);
        var inv = await ctx.SalesInvoices.Include(i => i.Lines).FirstAsync(i => i.Id == invoiceId);
        Assert.Equal("RET-0001", inv.Number);
        Assert.Equal(new DateOnly(2026, 6, 1), inv.Date);
        Assert.Equal(new DateOnly(2026, 6, 15), inv.DueDate); // +14 days
        Assert.Equal(DocumentStatus.Draft, inv.Status);       // default: review before posting
        Assert.Equal(600m, inv.GrossTotal);                   // 500 + 20% VAT

        var t = await ctx.RecurringInvoices.FirstAsync(x => x.Id == id);
        Assert.Equal(new DateOnly(2026, 7, 1), t.NextRunDate); // advanced one month
        Assert.Equal(1, t.GeneratedCount);
    }

    [Fact]
    public async Task CatchUp_GeneratesEveryMissedPeriod_UpToToday()
    {
        using var ctx = _db.Create();
        // First run Jan; generator "hasn't run" until mid-April → Jan, Feb, Mar, Apr = 4.
        var id = AddTemplate(ctx, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));

        var created = await Svc(ctx).RunTemplateAsync(_businessId, id, new DateOnly(2026, 4, 15), _user);

        Assert.Equal(4, created.Count);
        var numbers = await ctx.SalesInvoices.OrderBy(i => i.Number).Select(i => i.Number).ToListAsync();
        Assert.Equal(new[] { "RET-0001", "RET-0002", "RET-0003", "RET-0004" }, numbers);
        var t = await ctx.RecurringInvoices.FirstAsync(x => x.Id == id);
        Assert.Equal(new DateOnly(2026, 5, 1), t.NextRunDate);
    }

    [Fact]
    public async Task AutoPost_PostsImmediately_AndRespectsEndDateAndPause()
    {
        using var ctx = _db.Create();
        var id = AddTemplate(ctx, RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1),
            autoPost: true, end: new DateOnly(2026, 2, 15));

        // End date caps generation at Jan and Feb (Mar 1 > end).
        var created = await Svc(ctx).RunTemplateAsync(_businessId, id, new DateOnly(2026, 6, 1), _user);
        Assert.Equal(2, created.Count);
        Assert.All(await ctx.SalesInvoices.ToListAsync(), i => Assert.Equal(DocumentStatus.Posted, i.Status));

        // Pausing stops further generation even when due.
        var t = await ctx.RecurringInvoices.FirstAsync(x => x.Id == id);
        t.Paused = true; t.EndDate = null; await ctx.SaveChangesAsync();
        Assert.Empty(await Svc(ctx).RunTemplateAsync(_businessId, id, new DateOnly(2027, 1, 1), _user));
    }

    [Fact]
    public async Task RunAllDue_SweepsAcrossBusinesses_WithTenantPrimed()
    {
        using var ctx = _db.Create();
        AddTemplate(ctx, RecurrenceFrequency.Monthly, new DateOnly(2026, 6, 1));
        // A second business with its own due template.
        Guid otherBiz, otherCust;
        using (var seed = _db.Create())
        {
            var (b2, _, sales2, _, _) = TestDb.SeedBusiness(seed, "Second Ltd");
            otherBiz = b2.Id;
            var c2 = new Customer { Id = Guid.NewGuid(), BusinessId = b2.Id, Name = "Other client" };
            seed.Customers.Add(c2);
            seed.SaveChanges();
            otherCust = c2.Id;
            var t2 = new RecurringInvoice
            {
                Id = Guid.NewGuid(), BusinessId = otherBiz, CustomerId = otherCust,
                Name = "Weekly", NumberPrefix = "WK", NextNumber = 1,
                Frequency = RecurrenceFrequency.Weekly, PaymentTermsDays = 7,
                NextRunDate = new DateOnly(2026, 6, 1),
            };
            t2.Lines.Add(new RecurringInvoiceLine
            {
                Id = Guid.NewGuid(), RecurringInvoiceId = t2.Id, Description = "Weekly service",
                Quantity = 1, UnitPrice = 100, VatRate = VatRate.Zero, AccountId = sales2.Id,
            });
            seed.RecurringInvoices.Add(t2);
            seed.SaveChanges();
        }

        // Sweep as of a date that makes both due (one monthly, one weekly).
        var total = await Svc(ctx).RunAllDueAsync(new DateOnly(2026, 6, 8), _user);
        Assert.True(total >= 2, $"expected at least the monthly + one weekly, got {total}");
    }

    public void Dispose() => _db.Dispose();
}
