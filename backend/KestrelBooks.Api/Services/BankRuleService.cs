using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record RuleSuggestion(Guid LineId, Guid RuleId, string RuleName, Guid AccountId,
    string AccountCode, string AccountName, bool AutoPost);

public record RuleRunResult(int Matched, int Posted, List<RuleSuggestion> Suggestions);

public class BankRuleService
{
    private readonly AppDbContext _db;
    private readonly DocumentPostingService _docs;
    public BankRuleService(AppDbContext db, DocumentPostingService docs)
    {
        _db = db; _docs = docs;
    }

    /// <summary>
    /// Runs the rules over every unmatched statement line on an account.
    ///
    /// First matching rule wins, by priority then creation order, so a specific
    /// rule can be placed in front of a general one. Rules marked AutoPost create
    /// and post the money transaction outright; the rest come back as suggestions
    /// for a human to accept — which is the default, because a rule earns trust
    /// before it is allowed to post unsupervised.
    /// </summary>
    public async Task<RuleRunResult> ApplyAsync(Guid businessId, Guid bankAccountId, Guid userId)
    {
        var rules = await _db.BankRules
            .Where(r => r.BusinessId == businessId && r.Enabled)
            .OrderBy(r => r.Priority).ThenBy(r => r.CreatedAtUtc)
            .ToListAsync();
        if (rules.Count == 0) return new RuleRunResult(0, 0, new List<RuleSuggestion>());

        var lines = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.BankAccountId == bankAccountId
                        && l.Status == StatementLineStatus.Unmatched)
            .OrderBy(l => l.Date)
            .ToListAsync();

        var accounts = await _db.Accounts.Where(a => a.BusinessId == businessId)
            .ToDictionaryAsync(a => a.Id, a => new { a.Code, a.Name });

        var suggestions = new List<RuleSuggestion>();
        var matched = 0;
        var posted = 0;

        foreach (var line in lines)
        {
            var rule = rules.FirstOrDefault(r => r.Matches(line.Description, line.Amount, bankAccountId));
            if (rule is null) continue;
            matched++;

            if (!accounts.TryGetValue(rule.AccountId, out var account)) continue;

            if (rule.AutoPost)
            {
                var tx = new MoneyTransaction
                {
                    Id = Guid.NewGuid(),
                    BusinessId = businessId,
                    Direction = line.Amount > 0 ? MoneyDirection.In : MoneyDirection.Out,
                    Date = line.Date,
                    Reference = line.Description.Length > 60 ? line.Description[..60] : line.Description,
                    Amount = Math.Abs(line.Amount),
                    BankAccountId = line.BankAccountId,
                    DirectAccountId = rule.AccountId,
                    CustomerId = rule.CustomerId,
                    VendorId = rule.VendorId,
                    Notes = $"Auto-coded by rule \"{rule.Name}\"",
                };
                _db.MoneyTransactions.Add(tx);
                await _db.SaveChangesAsync();
                var journal = await _docs.PostMoneyTransactionAsync(businessId, tx.Id, userId);

                line.Status = StatementLineStatus.Matched;
                line.CreatedMoneyTransactionId = tx.Id;
                line.MatchedJournalLineId = journal.Lines.First(l => l.AccountId == line.BankAccountId).Id;
                rule.TimesApplied++;
                rule.LastAppliedUtc = DateTime.UtcNow;
                posted++;
            }
            else
            {
                suggestions.Add(new RuleSuggestion(line.Id, rule.Id, rule.Name, rule.AccountId,
                    account.Code, account.Name, false));
            }
        }

        if (posted > 0) await _db.SaveChangesAsync();
        return new RuleRunResult(matched, posted, suggestions);
    }

    /// <summary>
    /// Proposes a rule from a line the user has just coded by hand — the moment
    /// they are most likely to want one, and with the tedious part filled in.
    /// The suggested match text is the stable-looking part of the description:
    /// bank narratives usually end in a varying reference, so the leading words
    /// are what repeat.
    /// </summary>
    public static string SuggestMatchText(string description)
    {
        var cleaned = (description ?? string.Empty).Trim();
        if (cleaned.Length == 0) return cleaned;
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.All(char.IsDigit))          // drop pure reference numbers
            .Take(3)
            .ToArray();
        return words.Length > 0 ? string.Join(' ', words) : cleaned;
    }
}
