namespace KestrelBooks.Api.Domain;

public enum AccountType { Asset = 0, Liability = 1, Equity = 2, Income = 3, Expense = 4 }

/// <summary>
/// Well-known roles some accounts play in automatic posting.
/// Resolved by tag rather than hard-coded account codes so users can renumber their CoA freely.
/// </summary>
public static class SystemTags
{
    public const string TradeDebtors = "TRADE_DEBTORS";      // sales ledger control account
    public const string TradeCreditors = "TRADE_CREDITORS";  // purchase ledger control account
    public const string VatOutput = "VAT_OUTPUT";            // VAT on sales (liability)
    public const string VatInput = "VAT_INPUT";              // VAT on purchases (asset side of the VAT control)
    public const string DefaultBank = "BANK_DEFAULT";
    public const string RetainedEarnings = "RETAINED_EARNINGS";
    public const string AssetsUnderConstruction = "AUC";
    public const string StockRawMaterials = "STOCK_RM";
    public const string StockWip = "STOCK_WIP";
    public const string StockFinishedGoods = "STOCK_FG";
    public const string CostOfGoodsSold = "COGS";
    public const string LabourAbsorbed = "LABOUR_ABSORBED";
    public const string OverheadAbsorbed = "OVERHEAD_ABSORBED";
    public const string StockAdjustments = "STOCK_ADJ";
}

/// <summary>A general ledger (nominal) account. Fully user-customisable per business.</summary>
public class Account
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Code { get; set; } = "";       // e.g. "1200" — Sage-style nominal code
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public string? SubType { get; set; }          // e.g. "Current Assets", "Overheads"
    public bool IsBank { get; set; }
    public string? SystemTag { get; set; }        // see SystemTags; null for ordinary accounts
    public bool Archived { get; set; }
}

public enum JournalStatus { Draft = 0, Posted = 1, Reversed = 2 }

public enum SourceType
{
    Manual = 0, SalesInvoice = 1, PurchaseInvoice = 2,
    Receipt = 3, Payment = 4, Depreciation = 5, AssetCapitalisation = 6, Reversal = 7,
    OpeningBalance = 8, SalesCreditNote = 9, PurchaseCreditNote = 10, YearEndClose = 11
}

/// <summary>
/// A journal entry (header). Draft journals are editable/deletable.
/// Posted journals are immutable — corrections require a reversal, preserving the audit trail.
/// </summary>
public class JournalEntry
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public int Number { get; set; }                       // sequential per business, assigned on posting
    public DateOnly Date { get; set; }
    public string Reference { get; set; } = "";
    public string Narrative { get; set; } = "";
    public JournalStatus Status { get; set; } = JournalStatus.Draft;
    public SourceType Source { get; set; } = SourceType.Manual;
    public Guid? SourceId { get; set; }                   // id of the invoice/receipt/asset run that produced it
    public Guid? ReversalOfId { get; set; }               // set on the reversing journal
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedBy { get; set; }
    public List<JournalLine> Lines { get; set; } = new();
}

/// <summary>One side of a double entry. Exactly one of Debit/Credit is non-zero.</summary>
public class JournalLine
{
    public Guid Id { get; set; }
    public Guid JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
}
