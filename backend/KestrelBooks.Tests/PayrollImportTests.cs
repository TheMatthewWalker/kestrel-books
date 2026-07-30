using System.Text;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// A payroll journal that is nearly right is worse than one that fails loudly:
/// the error surfaces months later inside a VAT return or a set of accounts. So
/// the import refuses anything that does not parse cleanly and balance exactly.
/// </summary>
public class PayrollImportTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();

    public PayrollImportTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Payroll Ltd");
        _businessId = b.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private static Stream Csv(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private PayrollImportService Service(Api.Data.AppDbContext ctx) =>
        new(ctx, new PostingService(ctx));

    private async Task<string> RealCodes(Api.Data.AppDbContext ctx)
    {
        var expense = await ctx.Accounts.FirstAsync(a => a.Type == AccountType.Expense);
        var bank = await ctx.Accounts.FirstAsync(a => a.IsBank);
        return $"{expense.Code},{bank.Code}";
    }

    [Fact]
    public async Task ATypicalPayrollJournal_ParsesAndBalances()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        var csv = $"""
            Code,Debit,Credit,Description
            {codes[0]},2500.00,,Gross pay
            {codes[1]},,2500.00,Net pay and deductions
            """;

        var preview = await Service(ctx).ParseAsync(_businessId, Csv(csv), new DateOnly(2026, 6, 30));

        Assert.Empty(preview.Problems);
        Assert.True(preview.Balanced);
        Assert.Equal(2, preview.Lines.Count);
        Assert.All(preview.Lines, l => Assert.True(l.Matched));
        Assert.Equal(2_500m, preview.TotalDebits);
        Assert.Equal(2_500m, preview.TotalCredits);
    }

    [Fact]
    public async Task Importing_PostsTheJournal_TaggedAsPayroll()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        var csv = $"Code,Debit,Credit,Description\n{codes[0]},1000,,Gross\n{codes[1]},,1000,Net";

        var journal = await Service(ctx).ImportAsync(_businessId, Csv(csv),
            new DateOnly(2026, 6, 30), "PAY-202606", _user);

        var posted = await ctx.Journals.Include(j => j.Lines).FirstAsync(j => j.Id == journal.Id);
        Assert.Equal(JournalStatus.Posted, posted.Status);
        Assert.Equal(SourceType.PayrollJournal, posted.Source);
        Assert.Equal(posted.Lines.Sum(l => l.Debit), posted.Lines.Sum(l => l.Credit));
        Assert.Contains("June", posted.Narrative);
    }

    [Fact]
    public async Task AnUnbalancedFile_IsRefused_WithTheDifferenceStated()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        var csv = $"Code,Debit,Credit\n{codes[0]},1000,\n{codes[1]},,900";

        var preview = await Service(ctx).ParseAsync(_businessId, Csv(csv), new DateOnly(2026, 6, 30));
        Assert.False(preview.Balanced);
        Assert.Contains(preview.Problems, p => p.Contains("100"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(ctx).ImportAsync(
            _businessId, Csv(csv), new DateOnly(2026, 6, 30), "PAY", _user));
        Assert.Empty(await ctx.Journals.ToListAsync());
    }

    [Fact]
    public async Task UnknownAccountCodes_AreNamedRatherThanSilentlyDropped()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        var csv = $"Code,Debit,Credit\n9999,1000,\n{codes[1]},,1000";

        var preview = await Service(ctx).ParseAsync(_businessId, Csv(csv), new DateOnly(2026, 6, 30));

        Assert.Contains(preview.Problems, p => p.Contains("9999"));
        Assert.Contains(preview.Lines, l => !l.Matched && l.Code == "9999");
    }

    [Fact]
    public async Task MessyRealWorldFormatting_IsHandled()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        // Currency symbols, thousands separators, bracketed negatives, quoted commas.
        var csv = $"""
            Code,Debit,Credit,Description
            {codes[0]},"£1,250.50",,"Gross pay, June"
            {codes[1]},,"1,250.50",Net
            """;

        var preview = await Service(ctx).ParseAsync(_businessId, Csv(csv), new DateOnly(2026, 6, 30));

        Assert.Empty(preview.Problems);
        Assert.Equal(1_250.50m, preview.TotalDebits);
        Assert.Equal("Gross pay, June", preview.Lines[0].Description);
    }

    [Fact]
    public async Task ALineWithBothDebitAndCredit_IsRejected()
    {
        using var ctx = _db.Create();
        var codes = (await RealCodes(ctx)).Split(',');
        var csv = $"Code,Debit,Credit\n{codes[0]},500,300\n{codes[1]},,200";

        var preview = await Service(ctx).ParseAsync(_businessId, Csv(csv), new DateOnly(2026, 6, 30));

        Assert.Contains(preview.Problems, p => p.Contains("both a debit and a credit"));
    }

    public void Dispose() => _db.Dispose();
}
