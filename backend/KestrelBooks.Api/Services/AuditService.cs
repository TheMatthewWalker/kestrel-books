using System.Text.Json;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Builds audit entries from EF's change tracker. Called from SaveChanges, so
/// every write goes through it whether the caller remembered or not.
/// </summary>
public static class AuditBuilder
{
    /// <summary>
    /// Entities worth auditing: the mutable business records. Journals and journal
    /// lines are excluded on purpose — a posted journal never changes, and the
    /// reversal chain is already a better audit trail than a field diff.
    /// </summary>
    private static readonly HashSet<string> Audited = new()
    {
        "SalesInvoice", "PurchaseInvoice", "SalesCreditNote", "PurchaseCreditNote",
        "SalesInvoiceLine", "PurchaseInvoiceLine",
        "Customer", "Vendor", "Item", "Account", "FixedAsset",
        "Business", "UserBusinessAccess", "RecurringInvoice", "PeriodEndSchedule",
        "MoneyTransaction", "BankRule", "Budget", "TrackingCategory",
    };

    /// <summary>Never record these — either noise or sensitive.</summary>
    private static readonly HashSet<string> SkipProperties = new()
    {
        "Id", "BusinessId", "CreatedAtUtc", "ConcurrencyToken",
        "AccessToken", "RefreshToken", "TokenHash", "TotpSecret", "PasswordHash",
    };

    public static List<AuditEntry> Collect(ChangeTracker tracker, Guid userId, string? userName)
    {
        var entries = new List<AuditEntry>();

        foreach (var e in tracker.Entries())
        {
            var typeName = e.Metadata.ClrType.Name;
            if (!Audited.Contains(typeName)) continue;
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var businessId = TryGetBusinessId(e);
            if (businessId == Guid.Empty) continue;

            var entityId = e.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue;
            var changes = new Dictionary<string, object>();

            if (e.State == EntityState.Modified)
            {
                foreach (var p in e.Properties)
                {
                    if (!p.IsModified || SkipProperties.Contains(p.Metadata.Name)) continue;
                    if (Equals(p.OriginalValue, p.CurrentValue)) continue;
                    changes[p.Metadata.Name] = new
                    {
                        from = p.OriginalValue?.ToString(),
                        to = p.CurrentValue?.ToString(),
                    };
                }
                if (changes.Count == 0) continue;   // nothing meaningful changed
            }
            else if (e.State == EntityState.Added)
            {
                foreach (var p in e.Properties)
                {
                    if (SkipProperties.Contains(p.Metadata.Name)) continue;
                    if (p.CurrentValue is null) continue;
                    changes[p.Metadata.Name] = new { from = (string?)null, to = p.CurrentValue.ToString() };
                }
            }

            entries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                EntityType = typeName,
                EntityId = entityId is Guid g ? g : Guid.Empty,
                Action = e.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Updated,
                },
                Changes = JsonSerializer.Serialize(changes),
                UserId = userId,
                UserName = userName,
            });
        }

        return entries;
    }

    private static Guid TryGetBusinessId(EntityEntry e)
    {
        var prop = e.Properties.FirstOrDefault(p => p.Metadata.Name == "BusinessId");
        if (prop?.CurrentValue is Guid g && g != Guid.Empty) return g;
        // Business itself: its own id is the tenant.
        if (e.Metadata.ClrType.Name == "Business"
            && e.Properties.FirstOrDefault(p => p.Metadata.Name == "Id")?.CurrentValue is Guid bid)
            return bid;
        // Lines: fall back to the parent's tenant if it is being tracked.
        return Guid.Empty;
    }
}
