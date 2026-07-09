using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The most important tests in the codebase: prove the global query filters
/// mean tenant A can never see tenant B, even through a query with no
/// explicit Where clause at all.
/// </summary>
public class TenantIsolationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessA;
    private readonly Guid _businessB;

    public TenantIsolationTests()
    {
        using var ctx = _db.Create(); // no tenant set: seeding is unfiltered
        var (a, debtorsA, salesA, _, _) = TestDb.SeedBusiness(ctx, "Alpha Ltd");
        var (b, debtorsB, salesB, _, _) = TestDb.SeedBusiness(ctx, "Beta Ltd");
        _businessA = a.Id; _businessB = b.Id;

        ctx.Customers.Add(new Customer { Id = Guid.NewGuid(), BusinessId = a.Id, Name = "Alpha Customer" });
        ctx.Customers.Add(new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Beta Customer" });
        ctx.Journals.Add(new JournalEntry
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, Date = new DateOnly(2026, 1, 15),
            Reference = "B-1", Narrative = "Beta journal", Status = JournalStatus.Posted, Number = 1,
            Lines =
            {
                new JournalLine { Id = Guid.NewGuid(), AccountId = debtorsB.Id, Debit = 120, Credit = 0 },
                new JournalLine { Id = Guid.NewGuid(), AccountId = salesB.Id, Debit = 0, Credit = 120 },
            }
        });
        ctx.SaveChanges();
    }

    [Fact]
    public void UnfilteredQuery_ReturnsOnlyCurrentTenant()
    {
        _db.Tenant.Set(_businessA, BusinessRole.Owner);
        using var ctx = _db.Create();
        // Deliberately no Where clause — the filter must do the work.
        var customers = ctx.Customers.ToList();
        Assert.Single(customers);
        Assert.Equal("Alpha Customer", customers[0].Name);
    }

    [Fact]
    public void JournalLines_FilterThroughNavigation()
    {
        _db.Tenant.Set(_businessA, BusinessRole.Owner);
        using var ctx = _db.Create();
        Assert.Empty(ctx.JournalLines.ToList()); // Beta's lines are invisible to Alpha
        _db.Tenant.Set(_businessB, BusinessRole.Owner);
        using var ctx2 = _db.Create();
        Assert.Equal(2, ctx2.JournalLines.Count());
    }

    [Fact]
    public void FindById_AcrossTenants_ReturnsNothing()
    {
        Guid betaCustomerId;
        _db.Tenant.Set(_businessB, BusinessRole.Owner);
        using (var ctx = _db.Create()) betaCustomerId = ctx.Customers.First().Id;

        _db.Tenant.Set(_businessA, BusinessRole.Owner);
        using var ctxA = _db.Create();
        // Knowing another tenant's ID must not be enough.
        Assert.Null(ctxA.Customers.FirstOrDefault(c => c.Id == betaCustomerId));
    }

    [Fact]
    public void AccessService_EnforcesRoleRank()
    {
        var tenant = new TenantProvider();
        tenant.Set(_businessA, BusinessRole.Bookkeeper);
        var access = new AccessService(tenant);
        var user = new System.Security.Claims.ClaimsPrincipal();

        // Bookkeeper can do bookkeeping…
        access.EnsureAccessAsync(user, _businessA, BusinessRole.Bookkeeper).Wait();
        // …but cannot submit to HMRC (Accountant) or manage users (Owner).
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => access.EnsureAccessAsync(user, _businessA, BusinessRole.Accountant)).Wait();
        Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => access.EnsureAccessAsync(user, _businessA, BusinessRole.Owner)).Wait();
        // Accountant outranks Bookkeeper despite a higher enum value (3 vs 1).
        tenant.Set(_businessA, BusinessRole.Accountant);
        access.EnsureAccessAsync(user, _businessA, BusinessRole.Bookkeeper).Wait();
    }

    public void Dispose() => _db.Dispose();
}
