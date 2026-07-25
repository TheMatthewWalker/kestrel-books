using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The reconciliation must explain the gap between the books and the bank, not
/// just list transactions. The classic case: a cheque written and posted but not
/// yet presented — the ledger is lower than the bank, and by exactly that amount.
/// </summary>
public class BankReconciliationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _bank, _sales, _debtors;

    public BankReconciliationTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, sales, bank, _) = TestDb.SeedBusiness(ctx, "Recon Ltd");
        _businessId = b.Id; _bank = bank.Id; _sales = sales.Id; _debtors = debtors.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private async Task<Guid> PostToBank(Api.Data.AppDbContext ctx, DateOnly date, decimal amount, string narrative)
    {
        var posting = new PostingService(ctx);
        var journal = await posting.CreateDraftAsync(_businessId, _user, date, "REF", narrative,
            SourceType.Manual, null,
            amount >= 0
                ? new[] { new DraftLine(_bank, amount, 0, narrative), new DraftLine(_sales, 0, amount, narrative) }
                : new[] { new DraftLine(_debtors, -amount, 0, narrative), new DraftLine(_bank, 0, -amount, narrative) });
        await posting.PostAsync(_businessId, journal.Id, _user);
        var line = journal.Lines.First(l => l.AccountId == _bank);
        return line.Id;
    }

    private void AddStatementLine(Api.Data.AppDbContext ctx, DateOnly date, decimal amount,
        string description, StatementLineStatus status, Guid? matchedLineId = null)
    {
        var import = new BankStatementImport
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, BankAccountId = _bank,
            FileName = "stmt.csv", ImportedAtUtc = DateTime.UtcNow,
        };
        ctx.BankStatementImports.Add(import);
        ctx.BankStatementLines.Add(new BankStatementLine
        {
            Id = Guid.NewGuid(), ImportId = import.Id, BusinessId = _businessId,
            BankAccountId = _bank, Date = date, Description = description,
            Amount = amount, Status = status, MatchedJournalLineId = matchedLineId,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task UnpresentedPayment_ExplainsTheDifference_AndTheAccountReconciles()
    {
        using var ctx = _db.Create();
        // Two receipts, both on the statement and matched. One payment out, posted
        // but not yet presented at the bank.
        var r1 = await PostToBank(ctx, new DateOnly(2026, 6, 1), 1_000m, "Receipt one");
        var r2 = await PostToBank(ctx, new DateOnly(2026, 6, 5), 500m, "Receipt two");
        await PostToBank(ctx, new DateOnly(2026, 6, 28), -300m, "Cheque 1041 to supplier");

        AddStatementLine(ctx, new DateOnly(2026, 6, 1), 1_000m, "RECEIPT ONE", StatementLineStatus.Matched, r1);
        AddStatementLine(ctx, new DateOnly(2026, 6, 5), 500m, "RECEIPT TWO", StatementLineStatus.Matched, r2);

        // The bank says 1,500; the ledger says 1,200 because of the unpresented cheque.
        var rec = await new BankReconciliationService(ctx)
            .BuildAsync(_businessId, _bank, new DateOnly(2026, 6, 30), 1_500m);

        Assert.Equal(1_200m, rec.LedgerBalance);
        Assert.Equal(1_500m, rec.StatementBalance);
        Assert.Equal(-300m, rec.UnpresentedLedgerTotal);
        Assert.Equal(0m, rec.UnmatchedStatementTotal);
        Assert.Equal(0m, rec.Difference);
        Assert.True(rec.Reconciled);

        var item = Assert.Single(rec.UnpresentedLedgerItems);
        Assert.Equal(-300m, item.Amount);
        Assert.Contains("1041", item.Description);
    }

    [Fact]
    public async Task StatementLineNotYetInTheLedger_IsListedAsUnmatched()
    {
        using var ctx = _db.Create();
        var r1 = await PostToBank(ctx, new DateOnly(2026, 6, 1), 1_000m, "Receipt one");
        AddStatementLine(ctx, new DateOnly(2026, 6, 1), 1_000m, "RECEIPT ONE", StatementLineStatus.Matched, r1);
        // Bank charge on the statement that nobody has posted yet.
        AddStatementLine(ctx, new DateOnly(2026, 6, 20), -18m, "ACCOUNT FEE", StatementLineStatus.Unmatched);

        var rec = await new BankReconciliationService(ctx)
            .BuildAsync(_businessId, _bank, new DateOnly(2026, 6, 30), 982m);

        Assert.Equal(1_000m, rec.LedgerBalance);
        Assert.Equal(-18m, rec.UnmatchedStatementTotal);
        Assert.Equal(0m, rec.Difference);
        Assert.True(rec.Reconciled);
        Assert.Single(rec.UnmatchedStatementLines);
    }

    [Fact]
    public async Task AGenuineDiscrepancy_IsReportedNotHidden()
    {
        using var ctx = _db.Create();
        var r1 = await PostToBank(ctx, new DateOnly(2026, 6, 1), 1_000m, "Receipt one");
        AddStatementLine(ctx, new DateOnly(2026, 6, 1), 1_000m, "RECEIPT ONE", StatementLineStatus.Matched, r1);

        // The bank says 1,250 with nothing to explain the extra 250.
        var rec = await new BankReconciliationService(ctx)
            .BuildAsync(_businessId, _bank, new DateOnly(2026, 6, 30), 1_250m);

        Assert.Equal(-250m, rec.Difference);
        Assert.False(rec.Reconciled);
    }

    [Fact]
    public async Task ExcludedLines_AreIgnoredEntirely()
    {
        using var ctx = _db.Create();
        var r1 = await PostToBank(ctx, new DateOnly(2026, 6, 1), 1_000m, "Receipt one");
        AddStatementLine(ctx, new DateOnly(2026, 6, 1), 1_000m, "RECEIPT ONE", StatementLineStatus.Matched, r1);
        AddStatementLine(ctx, new DateOnly(2026, 6, 9), -50m, "DUPLICATE IMPORT", StatementLineStatus.Excluded);

        var rec = await new BankReconciliationService(ctx)
            .BuildAsync(_businessId, _bank, new DateOnly(2026, 6, 30), 1_000m);

        Assert.Equal(0m, rec.UnmatchedStatementTotal);
        Assert.Empty(rec.UnmatchedStatementLines);
        Assert.True(rec.Reconciled);
    }

    [Fact]
    public async Task TransactionsAfterTheDate_AreExcludedFromBothSides()
    {
        using var ctx = _db.Create();
        await PostToBank(ctx, new DateOnly(2026, 6, 1), 1_000m, "In period");
        await PostToBank(ctx, new DateOnly(2026, 7, 3), 700m, "After the date");

        var rec = await new BankReconciliationService(ctx)
            .BuildAsync(_businessId, _bank, new DateOnly(2026, 6, 30), null);

        Assert.Equal(1_000m, rec.LedgerBalance);
    }

    public void Dispose() => _db.Dispose();
}
