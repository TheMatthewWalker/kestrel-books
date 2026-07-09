using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Tests;

/// <summary>
/// SQLite-in-memory context factory. The TenantProvider is exposed so tests
/// can switch tenants against the same seeded database — exactly what the
/// isolation tests need to prove.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public TenantProvider Tenant { get; } = new();

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var ctx = Create();
        ctx.Database.EnsureCreated();
    }

    public AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, Tenant);

    public void Dispose() => _conn.Dispose();

    public static (Business business, Account debtors, Account sales, Account bank, Account vatOut) SeedBusiness(
        AppDbContext db, string name)
    {
        var business = new Business { Id = Guid.NewGuid(), Name = name };
        db.Businesses.Add(business);
        var accounts = CoaSeeder.DefaultChart(business.Id);
        db.Accounts.AddRange(accounts);
        db.SaveChanges();
        Account ByTag(string tag) => accounts.First(a => a.SystemTag == tag);
        return (business, ByTag(SystemTags.TradeDebtors), accounts.First(a => a.Code == "4000"),
                ByTag(SystemTags.DefaultBank), ByTag(SystemTags.VatOutput));
    }
}
