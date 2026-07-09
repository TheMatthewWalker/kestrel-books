using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record MatchSuggestion(Guid JournalLineId, Guid JournalId, int JournalNumber,
    DateOnly Date, string Narrative, decimal Amount);

/// <summary>
/// Bank statement import (CSV / OFX) and reconciliation matching.
///
/// CSV parsing is header-driven and tolerant of the common UK bank layouts:
///   Date, Description, Amount [, Balance]
///   Date, Description, Debit, Credit [, Balance]
///   Date, Type, Description, Value, Balance
/// Dates accept dd/MM/yyyy, dd-MM-yyyy and yyyy-MM-dd.
///
/// Matching suggests posted journal lines on the same bank account with the
/// exact amount on the correct side, within ±7 days, not already matched.
/// </summary>
public class BankImportService
{
    private readonly AppDbContext _db;
    public BankImportService(AppDbContext db) => _db = db;

    public async Task<BankStatementImport> ImportAsync(
        Guid businessId, Guid bankAccountId, string fileName, string content)
    {
        var isOfx = fileName.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("<OFX", StringComparison.OrdinalIgnoreCase);
        var parsed = isOfx ? ParseOfx(content) : ParseCsv(content);
        if (parsed.Count == 0)
            throw new InvalidOperationException("No transactions found in the file. Check it is a bank statement export (CSV or OFX).");

        // Dedupe against previously imported lines for this account.
        var existingRefs = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.BankAccountId == bankAccountId)
            .Select(l => l.ExternalRef).ToListAsync();
        var refs = existingRefs.ToHashSet();

        var import = new BankStatementImport
        {
            Id = Guid.NewGuid(), BusinessId = businessId, BankAccountId = bankAccountId,
            FileName = fileName, Source = isOfx ? StatementSource.Ofx : StatementSource.Csv,
        };
        foreach (var (date, desc, amount, extRef) in parsed)
        {
            var reference = extRef ?? Hash($"{date:yyyy-MM-dd}|{desc}|{amount:0.00}");
            if (refs.Contains(reference)) continue; // already imported
            refs.Add(reference);
            import.Lines.Add(new BankStatementLine
            {
                Id = Guid.NewGuid(), ImportId = import.Id, BusinessId = businessId,
                BankAccountId = bankAccountId, Date = date, Description = desc,
                Amount = amount, ExternalRef = reference,
            });
        }
        import.LineCount = import.Lines.Count;
        _db.BankStatementImports.Add(import);
        await _db.SaveChangesAsync();
        return import;
    }

    public async Task<Dictionary<Guid, List<MatchSuggestion>>> SuggestAsync(Guid businessId, Guid bankAccountId)
    {
        var lines = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.BankAccountId == bankAccountId
                        && l.Status == StatementLineStatus.Unmatched)
            .ToListAsync();
        if (lines.Count == 0) return new();

        var minDate = lines.Min(l => l.Date).AddDays(-7);
        var maxDate = lines.Max(l => l.Date).AddDays(7);

        var alreadyMatched = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.MatchedJournalLineId != null)
            .Select(l => l.MatchedJournalLineId!.Value).ToListAsync();
        var taken = alreadyMatched.ToHashSet();

        var candidates = await _db.JournalLines
            .Where(jl => jl.AccountId == bankAccountId
                         && jl.JournalEntry.BusinessId == businessId
                         && jl.JournalEntry.Status == JournalStatus.Posted
                         && jl.JournalEntry.Date >= minDate && jl.JournalEntry.Date <= maxDate)
            .Select(jl => new
            {
                jl.Id, jl.Debit, jl.Credit,
                JournalId = jl.JournalEntry.Id,
                jl.JournalEntry.Number,
                jl.JournalEntry.Date,
                jl.JournalEntry.Narrative
            })
            .ToListAsync();

        var result = new Dictionary<Guid, List<MatchSuggestion>>();
        foreach (var line in lines)
        {
            var abs = Math.Abs(line.Amount);
            var matches = candidates
                .Where(c => !taken.Contains(c.Id)
                            && (line.Amount > 0 ? c.Debit == abs : c.Credit == abs)
                            && Math.Abs(c.Date.DayNumber - line.Date.DayNumber) <= 7)
                .OrderBy(c => Math.Abs(c.Date.DayNumber - line.Date.DayNumber))
                .Take(3)
                .Select(c => new MatchSuggestion(c.Id, c.JournalId, c.Number, c.Date, c.Narrative,
                    line.Amount > 0 ? c.Debit : c.Credit))
                .ToList();
            if (matches.Count > 0) result[line.Id] = matches;
        }
        return result;
    }

    public async Task MatchAsync(Guid businessId, Guid statementLineId, Guid journalLineId)
    {
        var line = await _db.BankStatementLines
            .FirstOrDefaultAsync(l => l.Id == statementLineId && l.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Statement line not found.");
        var jl = await _db.JournalLines.Include(j => j.JournalEntry)
            .FirstOrDefaultAsync(j => j.Id == journalLineId && j.JournalEntry.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Journal line not found.");
        if (jl.AccountId != line.BankAccountId)
            throw new InvalidOperationException("That journal line is not on the same bank account.");
        var expected = Math.Abs(line.Amount);
        var actual = line.Amount > 0 ? jl.Debit : jl.Credit;
        if (actual != expected)
            throw new InvalidOperationException($"Amounts differ: statement {expected:0.00} vs ledger {actual:0.00}.");
        if (await _db.BankStatementLines.AnyAsync(l => l.MatchedJournalLineId == journalLineId))
            throw new InvalidOperationException("That journal line is already reconciled to another statement line.");

        line.Status = StatementLineStatus.Matched;
        line.MatchedJournalLineId = journalLineId;
        await _db.SaveChangesAsync();
    }

    private static List<(DateOnly date, string desc, decimal amount, string? extRef)> ParseCsv(string content)
    {
        var rows = content.Replace("\r\n", "\n").Split('\n')
            .Where(r => !string.IsNullOrWhiteSpace(r)).Select(SplitCsvRow).ToList();
        if (rows.Count < 2) return new();

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Any(n => h.Contains(n)));

        int dateCol = Col("date");
        int descCol = Col("description", "narrative", "details", "reference", "memo", "type");
        int amountCol = Col("amount", "value");
        int debitCol = Col("debit", "paid out", "money out", "withdraw");
        int creditCol = Col("credit", "paid in", "money in", "deposit");
        if (dateCol < 0) return new();

        var list = new List<(DateOnly, string, decimal, string?)>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count <= dateCol || !TryDate(row[dateCol], out var date)) continue;
            var desc = descCol >= 0 && row.Count > descCol ? row[descCol].Trim() : "";
            decimal amount;
            if (amountCol >= 0 && row.Count > amountCol && TryAmount(row[amountCol], out var a))
                amount = a;
            else
            {
                var dr = debitCol >= 0 && row.Count > debitCol && TryAmount(row[debitCol], out var d) ? d : 0;
                var cr = creditCol >= 0 && row.Count > creditCol && TryAmount(row[creditCol], out var c) ? c : 0;
                if (dr == 0 && cr == 0) continue;
                amount = cr - dr; // money in positive, money out negative
            }
            if (amount == 0) continue;
            list.Add((date, desc, Math.Round(amount, 2), null));
        }
        return list;
    }

    private static List<(DateOnly date, string desc, decimal amount, string? extRef)> ParseOfx(string content)
    {
        var list = new List<(DateOnly, string, decimal, string?)>();
        foreach (Match tx in Regex.Matches(content, @"<STMTTRN>(.*?)</STMTTRN>",
                     RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string? Tag(string name)
            {
                var m = Regex.Match(tx.Groups[1].Value, $@"<{name}>([^<\r\n]+)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            var dt = Tag("DTPOSTED");
            var amt = Tag("TRNAMT");
            if (dt is null || amt is null || dt.Length < 8) continue;
            if (!int.TryParse(dt[..4], out var y) || !int.TryParse(dt[4..6], out var mo) ||
                !int.TryParse(dt[6..8], out var d)) continue;
            if (!TryAmount(amt, out var amount) || amount == 0) continue;
            list.Add((new DateOnly(y, mo, d), Tag("NAME") ?? Tag("MEMO") ?? "", Math.Round(amount, 2), Tag("FITID")));
        }
        return list;
    }

    private static List<string> SplitCsvRow(string row)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in row)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private static bool TryDate(string s, out DateOnly date)
    {
        s = s.Trim().Trim('"');
        foreach (var f in new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "d/M/yyyy", "dd MMM yyyy" })
            if (DateOnly.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        return DateOnly.TryParse(s, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out date);
    }

    private static bool TryAmount(string s, out decimal amount) =>
        decimal.TryParse(s.Trim().Trim('"').Replace("£", "").Replace(",", ""),
            NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
