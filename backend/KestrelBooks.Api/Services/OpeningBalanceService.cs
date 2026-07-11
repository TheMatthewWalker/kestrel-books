using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record TbLine(Guid AccountId, decimal Debit, decimal Credit);
public record TbCsvRow(string Code, decimal Debit, decimal Credit);
public record TbParseResult(List<object> Matched, List<TbCsvRow> Unmatched, decimal TotalDebits, decimal TotalCredits);
public record OpeningInvoiceRequest(Guid ContactId, string Number, DateOnly Date, DateOnly DueDate, decimal Gross);
public record OpeningStockLine(Guid ItemId, decimal Quantity, decimal UnitCost);

/// <summary>
/// Client conversion. The opening trial balance becomes one posted journal
/// (Source = OpeningBalance) dated the day before go-live. Open invoices and
/// opening stock carry NO journals of their own — their values are already
/// inside the TB's control-account and stock lines; they exist so settlement,
/// aged analysis and perpetual inventory work from day one.
/// </summary>
public class OpeningBalanceService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public OpeningBalanceService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    public async Task<JournalEntry> ImportTrialBalanceAsync(
        Guid businessId, DateOnly conversionDate, IReadOnlyList<TbLine> lines, Guid userId)
    {
        if (await _db.Journals.AnyAsync(j =>
                j.BusinessId == businessId && j.Source == SourceType.OpeningBalance
                && j.Status != JournalStatus.Reversed))
            throw new InvalidOperationException(
                "An opening trial balance already exists. Reverse that journal first if the conversion needs redoing.");

        var drafts = lines
            .Where(l => l.Debit != 0 || l.Credit != 0)
            .Select(l => new DraftLine(l.AccountId, l.Debit, l.Credit, "Opening balance"))
            .ToList();

        var journal = await _posting.CreateDraftAsync(businessId, userId,
            conversionDate.AddDays(-1), "OPENING", "Opening trial balance (conversion)",
            SourceType.OpeningBalance, null, drafts);
        await _posting.PostAsync(businessId, journal.Id, userId);
        return journal;
    }

    /// <summary>Matches CSV rows (code, debit, credit) to the chart by nominal code.</summary>
    public async Task<TbParseResult> ParseCsvAsync(Guid businessId, string content)
    {
        var accounts = await _db.Accounts
            .Where(a => a.BusinessId == businessId)
            .ToDictionaryAsync(a => a.Code);

        var matched = new List<object>();
        var unmatched = new List<TbCsvRow>();
        decimal dr = 0, cr = 0;

        foreach (var raw in content.Replace("\r\n", "\n").Split('\n').Skip(1))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var cells = raw.Split(',');
            if (cells.Length < 3) continue;
            var code = cells[0].Trim().Trim('"');
            decimal.TryParse(cells[^2].Trim().Trim('"').Replace("£", ""), out var debit);
            decimal.TryParse(cells[^1].Trim().Trim('"').Replace("£", ""), out var credit);
            if (debit == 0 && credit == 0) continue;
            dr += debit; cr += credit;

            if (accounts.TryGetValue(code, out var acc))
                matched.Add(new { accountId = acc.Id, acc.Code, acc.Name, debit, credit });
            else
                unmatched.Add(new TbCsvRow(code, debit, credit));
        }
        return new TbParseResult(matched, unmatched, dr, cr);
    }

    public async Task<Guid> AddOpeningSalesInvoiceAsync(Guid businessId, OpeningInvoiceRequest req)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = req.ContactId,
            Number = req.Number, Date = req.Date, DueDate = req.DueDate,
            NetTotal = req.Gross, VatTotal = 0, GrossTotal = req.Gross,
            Status = DocumentStatus.Posted, IsOpeningBalance = true,
            Notes = "Opening balance — brought forward from previous system",
        };
        _db.SalesInvoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv.Id;
    }

    public async Task<Guid> AddOpeningPurchaseInvoiceAsync(Guid businessId, OpeningInvoiceRequest req)
    {
        var inv = new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = businessId, VendorId = req.ContactId,
            Number = req.Number, Date = req.Date, DueDate = req.DueDate,
            NetTotal = req.Gross, VatTotal = 0, GrossTotal = req.Gross,
            Status = DocumentStatus.Posted, IsOpeningBalance = true,
            Notes = "Opening balance — brought forward from previous system",
        };
        _db.PurchaseInvoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv.Id;
    }

    public async Task SetOpeningStockAsync(Guid businessId, DateOnly conversionDate,
        IReadOnlyList<OpeningStockLine> lines, StockService stock)
    {
        foreach (var line in lines.Where(l => l.Quantity > 0))
        {
            var item = await _db.Items.FirstOrDefaultAsync(i =>
                    i.Id == line.ItemId && i.BusinessId == businessId && i.TrackStock)
                ?? throw new KeyNotFoundException("Stock-tracked item not found.");
            if (item.QuantityOnHand != 0)
                throw new InvalidOperationException(
                    $"{item.Code} already has stock movements — opening quantities can only be set on empty items.");
            await stock.MoveAsync(item, conversionDate.AddDays(-1), StockMovementType.Opening,
                line.Quantity, line.UnitCost, null, null, "Opening stock (conversion)");
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Reconciliation view: do the entered open invoices agree with the TB control figures?</summary>
    public async Task<object> StatusAsync(Guid businessId)
    {
        var opening = await _db.Journals
            .Where(j => j.BusinessId == businessId && j.Source == SourceType.OpeningBalance
                        && j.Status != JournalStatus.Reversed)
            .Select(j => new { j.Id, j.Date, j.Number })
            .FirstOrDefaultAsync();

        decimal ControlFigure(string tag, List<(string? tag, decimal dr, decimal cr)> rows, bool debitNormal) =>
            rows.Where(r => r.tag == tag).Sum(r => debitNormal ? r.dr - r.cr : r.cr - r.dr);

        var tbRows = opening is null
            ? new List<(string?, decimal, decimal)>()
            : (await _db.JournalLines
                .Where(l => l.JournalEntryId == opening.Id && l.Account.SystemTag != null)
                .Select(l => new { l.Account.SystemTag, l.Debit, l.Credit }).ToListAsync())
              .Select(r => ((string?)r.SystemTag, r.Debit, r.Credit)).ToList();

        var openDebtors = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.IsOpeningBalance)
            .SumAsync(i => (decimal?)(i.GrossTotal - i.AmountPaid)) ?? 0;
        var openCreditors = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.IsOpeningBalance)
            .SumAsync(i => (decimal?)(i.GrossTotal - i.AmountPaid)) ?? 0;

        return new
        {
            hasOpeningJournal = opening != null,
            openingJournalNumber = opening?.Number,
            conversionDate = opening?.Date,
            tbDebtorsControl = ControlFigure(SystemTags.TradeDebtors, tbRows, debitNormal: true),
            tbCreditorsControl = ControlFigure(SystemTags.TradeCreditors, tbRows, debitNormal: false),
            enteredOpenDebtors = openDebtors,
            enteredOpenCreditors = openCreditors,
        };
    }
}
