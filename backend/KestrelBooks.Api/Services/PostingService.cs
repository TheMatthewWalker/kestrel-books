using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record DraftLine(Guid AccountId, decimal Debit, decimal Credit, string? Description);

/// <summary>
/// The double-entry engine. Every financial event in the system ultimately
/// flows through here as a balanced journal:
///   - drafts are editable and deletable;
///   - posting validates the entry balances (sum of debits == sum of credits),
///     assigns a sequential journal number and freezes it;
///   - posted journals can only be corrected by an automatic reversing journal,
///     preserving a complete audit trail.
/// </summary>
public class PostingService
{
    private readonly AppDbContext _db;
    public PostingService(AppDbContext db) => _db = db;

    public async Task<JournalEntry> CreateDraftAsync(
        Guid businessId, Guid userId, DateOnly date, string reference, string narrative,
        SourceType source, Guid? sourceId, IEnumerable<DraftLine> lines)
    {
        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Date = date,
            Reference = reference,
            Narrative = narrative,
            Source = source,
            SourceId = sourceId,
            CreatedBy = userId,
            Lines = lines.Select(l => new JournalLine
            {
                Id = Guid.NewGuid(),
                AccountId = l.AccountId,
                Debit = Math.Round(l.Debit, 2),
                Credit = Math.Round(l.Credit, 2),
                Description = l.Description
            }).ToList()
        };
        Validate(entry);
        _db.Journals.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public void Validate(JournalEntry e)
    {
        if (e.Lines.Count < 2)
            throw new InvalidOperationException("A journal needs at least two lines.");
        if (e.Lines.Any(l => l.Debit < 0 || l.Credit < 0))
            throw new InvalidOperationException("Amounts must be positive; use the opposite side instead of negatives.");
        if (e.Lines.Any(l => (l.Debit == 0) == (l.Credit == 0)))
            throw new InvalidOperationException("Each line must have exactly one of debit or credit.");
        var dr = e.Lines.Sum(l => l.Debit);
        var cr = e.Lines.Sum(l => l.Credit);
        if (dr != cr)
            throw new InvalidOperationException($"Journal does not balance: debits {dr:0.00} vs credits {cr:0.00}.");
        if (dr == 0)
            throw new InvalidOperationException("Journal total cannot be zero.");
    }

    public async Task<JournalEntry> PostAsync(Guid businessId, Guid journalId, Guid userId)
    {
        var entry = await _db.Journals.Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Journal not found.");
        if (entry.Status != JournalStatus.Draft)
            throw new InvalidOperationException("Only draft journals can be posted.");
        Validate(entry);
        entry.Status = JournalStatus.Posted;
        entry.PostedAtUtc = DateTime.UtcNow;
        entry.PostedBy = userId;
        await AssignNumberAndSaveAsync(entry);
        return entry;
    }

    /// <summary>Creates and immediately posts a mirror-image journal, linking both for the audit trail.</summary>
    public async Task<JournalEntry> ReverseAsync(Guid businessId, Guid journalId, Guid userId, DateOnly? reversalDate = null)
    {
        var original = await _db.Journals.Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == journalId && j.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Journal not found.");
        if (original.Status != JournalStatus.Posted)
            throw new InvalidOperationException("Only posted journals can be reversed.");

        var reversal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Date = reversalDate ?? original.Date,
            Reference = $"REV-{original.Number}",
            Narrative = $"Reversal of journal #{original.Number}: {original.Narrative}",
            Source = SourceType.Reversal,
            SourceId = original.Id,
            ReversalOfId = original.Id,
            CreatedBy = userId,
            Status = JournalStatus.Posted,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = userId,
            Lines = original.Lines.Select(l => new JournalLine
            {
                Id = Guid.NewGuid(),
                AccountId = l.AccountId,
                Debit = l.Credit,
                Credit = l.Debit,
                Description = l.Description
            }).ToList()
        };
        original.Status = JournalStatus.Reversed;
        _db.Journals.Add(reversal);
        await AssignNumberAndSaveAsync(reversal);
        return reversal;
    }

    public async Task<Account> RequireTaggedAccountAsync(Guid businessId, string tag) =>
        await _db.Accounts.FirstOrDefaultAsync(a => a.BusinessId == businessId && a.SystemTag == tag)
        ?? throw new InvalidOperationException($"No account tagged {tag} exists for this business.");

    /// <summary>
    /// Sequential numbering under concurrency: max+1 is racy, so the unique
    /// filtered index on (BusinessId, Number) is the arbiter — on a collision
    /// we take the next number and try again (bounded retries).
    /// </summary>
    private async Task AssignNumberAndSaveAsync(JournalEntry entry)
    {
        for (var attempt = 0; ; attempt++)
        {
            entry.Number = await NextNumberAsync(entry.BusinessId);
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Another poster claimed the number between our read and save.
            }
        }
    }

    private async Task<int> NextNumberAsync(Guid businessId)
    {
        var max = await _db.Journals.Where(j => j.BusinessId == businessId)
            .MaxAsync(j => (int?)j.Number) ?? 0;
        return max + 1;
    }
}
