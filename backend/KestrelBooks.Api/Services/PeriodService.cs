using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Period locking and year-end close.
///
/// Locking is a date fence: nothing may be created, posted or reversed on or
/// before Business.LockedThrough. Lock after filing a VAT return; unlock
/// deliberately (Accountant+) if history genuinely needs correcting.
///
/// Year-end close posts one journal (Source = YearEndClose) that zeroes every
/// income and expense account into Retained Earnings, then locks the year.
/// Because balances are computed cumulatively — including prior closes — the
/// second year's close automatically picks up only the new year's activity.
/// The P&L report excludes close journals (they're a transfer, not trading);
/// the TB and SoFP include them, which is exactly what makes the post-close
/// TB show zeroed P&L accounts.
/// </summary>
public class PeriodService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public PeriodService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    public async Task<object> StatusAsync(Guid businessId)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        var closes = await _db.Journals
            .Where(j => j.BusinessId == businessId && j.Source == SourceType.YearEndClose
                        && j.Status != JournalStatus.Reversed)
            .OrderBy(j => j.Date)
            .Select(j => new { j.Id, j.Date, j.Number })
            .ToListAsync();
        return new { lockedThrough = business.LockedThrough, yearEndsClosed = closes };
    }

    public async Task SetLockAsync(Guid businessId, DateOnly? through)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        business.LockedThrough = through;
        await _db.SaveChangesAsync();
    }

    public async Task<JournalEntry> CloseYearAsync(Guid businessId, DateOnly yearEnd, Guid userId)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        if (business.LockedThrough is DateOnly locked && yearEnd <= locked)
            throw new InvalidOperationException(
                $"The period up to {locked:dd MMM yyyy} is already locked — this year end is inside it.");
        if (await _db.Journals.AnyAsync(j => j.BusinessId == businessId
                && j.Source == SourceType.YearEndClose && j.Date == yearEnd
                && j.Status != JournalStatus.Reversed))
            throw new InvalidOperationException(
                "This year end has already been closed. Reverse the closing journal first to redo it.");

        // Cumulative P&L balances to the year end. Prior years' closing lines are
        // included in the sums, so only the unclosed residue remains — no need to
        // track which year a line "belongs" to. Aggregation is client-side:
        // SQLite (used in tests) can't translate decimal Sum, and a once-a-year
        // operation over one business's P&L lines is trivial in memory.
        var rawLines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Date <= yearEnd
                        && (l.JournalEntry.Status == JournalStatus.Posted
                            || l.JournalEntry.Status == JournalStatus.Reversed)
                        && (l.Account.Type == AccountType.Income || l.Account.Type == AccountType.Expense))
            .Select(l => new { l.AccountId, l.Account.Type, l.Debit, l.Credit })
            .ToListAsync();
        var balances = rawLines
            .GroupBy(l => new { l.AccountId, l.Type })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Type,
                Balance = g.Sum(x => x.Debit - x.Credit) // debit-positive raw balance
            })
            .ToList();

        var lines = new List<DraftLine>();
        decimal profit = 0; // credit-normal: positive = profit
        foreach (var b in balances.Where(b => b.Balance != 0))
        {
            // Zero the account: post the opposite of its raw balance.
            if (b.Balance < 0) lines.Add(new DraftLine(b.AccountId, -b.Balance, 0, "Year-end close"));
            else lines.Add(new DraftLine(b.AccountId, 0, b.Balance, "Year-end close"));
            profit -= b.Balance; // income has negative raw balance → adds to profit
        }
        if (lines.Count == 0)
            throw new InvalidOperationException("Nothing to close — no income or expense activity up to that date.");

        var retained = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.RetainedEarnings);
        if (profit > 0) lines.Add(new DraftLine(retained.Id, 0, profit, $"Profit for the year to {yearEnd:dd MMM yyyy}"));
        else lines.Add(new DraftLine(retained.Id, -profit, 0, $"Loss for the year to {yearEnd:dd MMM yyyy}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, yearEnd,
            $"YE-{yearEnd:yyyy}", $"Year-end close — {yearEnd:dd MMM yyyy}",
            SourceType.YearEndClose, null, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        business.LockedThrough = yearEnd; // closing a year locks it
        await _db.SaveChangesAsync();
        return journal;
    }
}
