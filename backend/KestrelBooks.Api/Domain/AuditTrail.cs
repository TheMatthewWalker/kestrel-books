namespace KestrelBooks.Api.Domain;

public enum AuditAction { Created = 0, Updated = 1, Deleted = 2 }

/// <summary>
/// Who changed what, when. Captured automatically at the SaveChanges boundary so
/// it cannot be forgotten by a service that takes a shortcut, and stored as the
/// changed fields only (old → new) rather than whole-row snapshots.
///
/// Deliberately excludes the ledger itself: journals are already immutable once
/// posted and corrected only by reversal, so their history is the ledger. This
/// covers the mutable things around it — invoices before posting, prices,
/// contacts, users, item and asset setup — which is exactly where a practice
/// gets asked "who changed this, and when?".
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public AuditAction Action { get; set; }
    /// <summary>JSON: { "field": { "from": "...", "to": "..." } } — changed fields only.</summary>
    public string Changes { get; set; } = "{}";
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
