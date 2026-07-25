using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record ReconciliationItem(DateOnly Date, string Description, decimal Amount, string Why);

public record BankReconciliation(
    string AccountName, DateOnly AsOf,
    decimal LedgerBalance,
    decimal StatementBalance,
    decimal MatchedStatementTotal,
    decimal UnmatchedStatementTotal,
    decimal UnpresentedLedgerTotal,
    decimal Difference,
    bool Reconciled,
    List<ReconciliationItem> UnmatchedStatementLines,
    List<ReconciliationItem> UnpresentedLedgerItems);

/// <summary>
/// The control an accountant actually signs: does the bank account in the ledger
/// agree to the bank's own balance, and if not, exactly which items explain the
/// gap? Matching individual lines is not the same thing — this proves the balance.
///
/// The reconciliation runs in both directions:
///   * statement lines imported but not yet matched to the ledger (money the bank
///     knows about that the books do not), and
///   * posted ledger entries with no matching statement line (unpresented cheques,
///     payments in transit — money the books know about that the bank has not shown).
///
/// Ledger balance − statement balance should equal unpresented ledger items less
/// unmatched statement lines. When it does, the account reconciles.
/// </summary>
public class BankReconciliationService
{
    private readonly AppDbContext _db;
    public BankReconciliationService(AppDbContext db) => _db = db;

    public async Task<BankReconciliation> BuildAsync(Guid businessId, Guid bankAccountId,
        DateOnly asOf, decimal? statementBalance)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == bankAccountId && a.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Bank account not found.");

        // Ledger balance: posted journal lines on this account up to the date.
        var ledgerLines = await _db.JournalLines
            .Where(l => l.AccountId == bankAccountId
                        && l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date <= asOf)
            .Select(l => new
            {
                l.Id, l.JournalEntry.Date, l.JournalEntry.Narrative,
                l.JournalEntry.Reference, l.Debit, l.Credit,
            })
            .ToListAsync();
        var ledgerBalance = ledgerLines.Sum(l => l.Debit - l.Credit);

        var statementLines = await _db.BankStatementLines
            .Where(s => s.BusinessId == businessId && s.BankAccountId == bankAccountId && s.Date <= asOf)
            .Select(s => new { s.Date, s.Description, s.Amount, s.Status, s.MatchedJournalLineId })
            .ToListAsync();

        var matched = statementLines.Where(s => s.Status == StatementLineStatus.Matched).ToList();
        var unmatched = statementLines.Where(s => s.Status == StatementLineStatus.Unmatched).ToList();

        // Which ledger lines have a statement line pointing at them?
        var presentedLineIds = matched.Where(m => m.MatchedJournalLineId != null)
            .Select(m => m.MatchedJournalLineId!.Value).ToHashSet();
        var unpresented = ledgerLines.Where(l => !presentedLineIds.Contains(l.Id)).ToList();

        // The bank's own balance: supplied by the user, or inferred from what has
        // been imported and matched (which is the best we can do without a feed).
        var statementBal = statementBalance ?? matched.Sum(m => m.Amount);

        var unmatchedTotal = unmatched.Sum(s => s.Amount);
        var unpresentedTotal = unpresented.Sum(l => l.Debit - l.Credit);

        // Ledger = statement + unpresented ledger items − unmatched statement lines.
        var difference = decimal.Round(
            ledgerBalance - (statementBal + unpresentedTotal - unmatchedTotal), 2,
            MidpointRounding.AwayFromZero);

        return new BankReconciliation(
            account.Name, asOf, ledgerBalance, statementBal,
            matched.Sum(m => m.Amount), unmatchedTotal, unpresentedTotal,
            difference, Math.Abs(difference) < 0.005m,
            unmatched.OrderBy(s => s.Date)
                .Select(s => new ReconciliationItem(s.Date, s.Description, s.Amount,
                    "On the statement, not yet in the ledger"))
                .ToList(),
            unpresented.OrderBy(l => l.Date)
                .Select(l => new ReconciliationItem(l.Date,
                    string.IsNullOrWhiteSpace(l.Narrative) ? l.Reference : l.Narrative,
                    l.Debit - l.Credit,
                    "In the ledger, not yet on the statement"))
                .ToList());
    }
}
