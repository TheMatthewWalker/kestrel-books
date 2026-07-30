using System.Globalization;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record PayrollLinePreview(string Code, string? AccountName, decimal Debit, decimal Credit,
    string? Description, bool Matched, string? Problem);

public record PayrollPreview(DateOnly Date, List<PayrollLinePreview> Lines,
    decimal TotalDebits, decimal TotalCredits, bool Balanced, List<string> Problems);

/// <summary>
/// Imports the journal a payroll package produces, rather than running payroll.
///
/// This is a deliberate scope decision: full RTI payroll is a regulated build
/// with real filing obligations, and BrightPay, Moneysoft and the rest already do
/// it well and cheaply. What practices actually need from their bookkeeping
/// system is for the resulting journal — gross pay, employer's NI, pension,
/// PAYE and net pay control — to land accurately without being retyped from a
/// PDF every month. That is a small feature that removes most of the pain.
///
/// The CSV is the shape every payroll package can export: account code, debit,
/// credit, and an optional description.
/// </summary>
public class PayrollImportService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public PayrollImportService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    public async Task<PayrollPreview> ParseAsync(Guid businessId, Stream csv, DateOnly date)
    {
        var accounts = await _db.Accounts.Where(a => a.BusinessId == businessId)
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToListAsync();
        var byCode = accounts.ToDictionary(a => a.Code.Trim(), a => a, StringComparer.OrdinalIgnoreCase);

        var lines = new List<PayrollLinePreview>();
        var problems = new List<string>();

        using var reader = new StreamReader(csv);
        var rowNumber = 0;
        string? row;
        while ((row = await reader.ReadLineAsync()) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(row)) continue;
            var cells = SplitCsv(row);
            if (cells.Count < 3) continue;

            // Skip a header row if the amounts aren't numbers.
            if (rowNumber == 1 && !TryMoney(cells[1], out _) && !TryMoney(cells[2], out _)) continue;

            var code = cells[0].Trim();
            TryMoney(cells[1], out var debit);
            TryMoney(cells[2], out var credit);
            var description = cells.Count > 3 ? cells[3].Trim() : null;

            if (debit != 0 && credit != 0)
            {
                problems.Add($"Row {rowNumber} ({code}) has both a debit and a credit — split it into two lines.");
                lines.Add(new PayrollLinePreview(code, null, debit, credit, description, false,
                    "Both debit and credit"));
                continue;
            }
            if (debit == 0 && credit == 0) continue;

            if (byCode.TryGetValue(code, out var account))
                lines.Add(new PayrollLinePreview(code, account.Name, debit, credit, description, true, null));
            else
            {
                problems.Add($"Row {rowNumber}: no account with code {code} in this client's chart.");
                lines.Add(new PayrollLinePreview(code, null, debit, credit, description, false,
                    "Unknown account code"));
            }
        }

        var totalDr = lines.Sum(l => l.Debit);
        var totalCr = lines.Sum(l => l.Credit);
        var balanced = Math.Abs(totalDr - totalCr) < 0.005m;
        if (!balanced)
            problems.Add($"The journal does not balance: debits {totalDr:N2} against credits {totalCr:N2}, "
                       + $"a difference of {Math.Abs(totalDr - totalCr):N2}.");
        if (lines.Count == 0)
            problems.Add("No usable rows found. Expected: account code, debit, credit, description.");

        return new PayrollPreview(date, lines, decimal.Round(totalDr, 2), decimal.Round(totalCr, 2),
            balanced, problems);
    }

    /// <summary>
    /// Posts the parsed journal. Refuses anything that did not parse cleanly —
    /// a payroll journal that is nearly right is not worth posting, because
    /// finding the error next month costs more than fixing the file now.
    /// </summary>
    public async Task<JournalEntry> ImportAsync(Guid businessId, Stream csv, DateOnly date,
        string reference, Guid userId)
    {
        var preview = await ParseAsync(businessId, csv, date);
        if (preview.Problems.Count > 0)
            throw new InvalidOperationException(string.Join(" ", preview.Problems));

        var accounts = await _db.Accounts.Where(a => a.BusinessId == businessId)
            .ToDictionaryAsync(a => a.Code.Trim().ToUpperInvariant(), a => a.Id);

        var draftLines = preview.Lines.Select(l => new DraftLine(
            accounts[l.Code.Trim().ToUpperInvariant()], l.Debit, l.Credit,
            l.Description ?? "Payroll")).ToList();

        var journal = await _posting.CreateDraftAsync(businessId, userId, date, reference,
            $"Payroll journal — {date:MMMM yyyy}", SourceType.PayrollJournal, null, draftLines);
        await _posting.PostAsync(businessId, journal.Id, userId);
        return journal;
    }

    private static bool TryMoney(string raw, out decimal value)
    {
        var cleaned = (raw ?? string.Empty).Trim()
            .Replace("£", "").Replace(",", "").Replace("(", "-").Replace(")", "");
        if (string.IsNullOrEmpty(cleaned)) { value = 0; return false; }
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells;
    }
}
