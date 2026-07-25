using System.Text.Json;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The audit trail is captured at the SaveChanges boundary, so these tests write
/// through ordinary entity operations — exactly as a service would — and check
/// the trail appeared without anyone asking for it.
/// </summary>
public class AuditTrailTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();

    public AuditTrailTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Audited Ltd");
        _businessId = b.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
        _db.Tenant.SetUser(_user, "matthew@practice.test");
    }

    [Fact]
    public void Creating_ARecord_LeavesACreatedEntry_WithTheActingUser()
    {
        using var ctx = _db.Create();
        ctx.Customers.Add(new Customer { Id = Guid.NewGuid(), BusinessId = _businessId, Name = "Acme" });
        ctx.SaveChanges();

        var audit = ctx.AuditEntries.Single(a => a.EntityType == "Customer");
        Assert.Equal(AuditAction.Created, audit.Action);
        Assert.Equal(_user, audit.UserId);
        Assert.Equal("matthew@practice.test", audit.UserName);
        Assert.Contains("Acme", audit.Changes);
    }

    [Fact]
    public void Updating_RecordsOnlyTheChangedFields_OldAndNew()
    {
        Guid customerId;
        using (var ctx = _db.Create())
        {
            var c = new Customer { Id = Guid.NewGuid(), BusinessId = _businessId, Name = "Before", PaymentTermsDays = 30 };
            ctx.Customers.Add(c);
            ctx.SaveChanges();
            customerId = c.Id;
        }

        using (var ctx = _db.Create())
        {
            var c = ctx.Customers.First(x => x.Id == customerId);
            c.Name = "After";
            c.PaymentTermsDays = 60;
            ctx.SaveChanges();
        }

        using var check = _db.Create();
        var audit = check.AuditEntries
            .Where(a => a.EntityId == customerId && a.Action == AuditAction.Updated)
            .OrderByDescending(a => a.AtUtc).First();

        using var doc = JsonDocument.Parse(audit.Changes);
        var root = doc.RootElement;
        Assert.Equal("Before", root.GetProperty("Name").GetProperty("from").GetString());
        Assert.Equal("After", root.GetProperty("Name").GetProperty("to").GetString());
        Assert.Equal("30", root.GetProperty("PaymentTermsDays").GetProperty("from").GetString());
        Assert.Equal("60", root.GetProperty("PaymentTermsDays").GetProperty("to").GetString());
        // Untouched fields are not recorded — the diff is the point.
        Assert.False(root.TryGetProperty("Email", out _));
    }

    [Fact]
    public void SavingWithNoRealChange_LeavesNoUpdateEntry()
    {
        Guid customerId;
        using (var ctx = _db.Create())
        {
            var c = new Customer { Id = Guid.NewGuid(), BusinessId = _businessId, Name = "Static" };
            ctx.Customers.Add(c);
            ctx.SaveChanges();
            customerId = c.Id;
        }

        using (var ctx = _db.Create())
        {
            var c = ctx.Customers.First(x => x.Id == customerId);
            c.Name = "Static";      // assigned, but identical
            ctx.SaveChanges();
        }

        using var check = _db.Create();
        Assert.Empty(check.AuditEntries.Where(a =>
            a.EntityId == customerId && a.Action == AuditAction.Updated));
    }

    [Fact]
    public void PostedJournals_AreNotAudited_BecauseTheLedgerIsItsOwnHistory()
    {
        using var ctx = _db.Create();
        var accounts = ctx.Accounts.Where(a => a.BusinessId == _businessId).Take(2).ToList();
        var journal = new JournalEntry
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Date = new DateOnly(2026, 6, 1),
            Reference = "J1", Narrative = "Test", CreatedBy = _user,
            Lines =
            {
                new JournalLine { Id = Guid.NewGuid(), AccountId = accounts[0].Id, Debit = 10 },
                new JournalLine { Id = Guid.NewGuid(), AccountId = accounts[1].Id, Credit = 10 },
            },
        };
        ctx.Journals.Add(journal);
        ctx.SaveChanges();

        Assert.Empty(ctx.AuditEntries.Where(a => a.EntityType == "JournalEntry"));
    }

    [Fact]
    public void AuditEntries_AreTenantIsolated()
    {
        using (var ctx = _db.Create())
        {
            ctx.Customers.Add(new Customer { Id = Guid.NewGuid(), BusinessId = _businessId, Name = "Mine" });
            ctx.SaveChanges();
        }

        Guid otherBusinessId;
        using (var ctx = _db.Create())
            otherBusinessId = TestDb.SeedBusiness(ctx, "Other Ltd").business.Id;
        _db.Tenant.Set(otherBusinessId, BusinessRole.Owner);

        using var ctx2 = _db.Create();
        Assert.Empty(ctx2.AuditEntries.Where(a => a.EntityType == "Customer"));
    }

    public void Dispose() => _db.Dispose();
}
