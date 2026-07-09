namespace KestrelBooks.Api.Domain;

public enum StatementSource { Csv = 0, Ofx = 1 }
public enum StatementLineStatus { Unmatched = 0, Matched = 1, Excluded = 2 }

/// <summary>One imported bank statement file.</summary>
public class BankStatementImport
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid BankAccountId { get; set; }
    public string FileName { get; set; } = "";
    public StatementSource Source { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public int LineCount { get; set; }
    public List<BankStatementLine> Lines { get; set; } = new();
}

/// <summary>
/// One line on a bank statement. Amount is signed from the bank's point of view:
/// positive = money in, negative = money out.
/// Reconciliation = pairing each line with a posted journal line on the same
/// bank account (Matched), or consciously setting it aside (Excluded).
/// </summary>
public class BankStatementLine
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }
    public BankStatementImport Import { get; set; } = null!;
    public Guid BusinessId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string? ExternalRef { get; set; }        // FITID from OFX, or row hash for CSV dedupe
    public StatementLineStatus Status { get; set; } = StatementLineStatus.Unmatched;
    public Guid? MatchedJournalLineId { get; set; }
    public Guid? CreatedMoneyTransactionId { get; set; }
}
