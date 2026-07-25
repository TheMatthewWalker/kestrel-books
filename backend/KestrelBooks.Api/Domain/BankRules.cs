namespace KestrelBooks.Api.Domain;

public enum RuleMatch { Contains = 0, StartsWith = 1, Exact = 2 }
public enum RuleDirection { Any = 0, MoneyIn = 1, MoneyOut = 2 }

/// <summary>
/// "When a statement line looks like this, code it to that."
///
/// The single biggest time sink in bookkeeping is re-coding the same forty
/// transactions every month. A rule turns that into a one-off decision: match on
/// the description the bank sends, optionally narrow by direction or an amount
/// range, and the line is coded — and optionally posted — without anyone looking
/// at it.
///
/// Rules are ordered by Priority (lowest first) so specific rules can sit in
/// front of general ones: "AMAZON*PRIME → Subscriptions" before "AMAZON → Office
/// supplies".
/// </summary>
public class BankRule
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Null = applies to every bank account.</summary>
    public Guid? BankAccountId { get; set; }

    public RuleMatch MatchType { get; set; } = RuleMatch.Contains;
    public string MatchText { get; set; } = "";
    public RuleDirection Direction { get; set; } = RuleDirection.Any;
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }

    /// <summary>Account the transaction is coded to.</summary>
    public Guid AccountId { get; set; }
    public VatRate VatRate { get; set; } = VatRate.OutsideScope;
    /// <summary>Optional contact to attach, so the supplier is recorded too.</summary>
    public Guid? VendorId { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// When true the rule posts the transaction outright; when false it only
    /// suggests, and a human confirms. Off by default — trust is earned per rule.
    /// </summary>
    public bool AutoPost { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public int TimesApplied { get; set; }
    public DateTime? LastAppliedUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool Matches(string description, decimal amount, Guid bankAccountId)
    {
        if (!Enabled) return false;
        if (BankAccountId is Guid acc && acc != bankAccountId) return false;
        if (Direction == RuleDirection.MoneyIn && amount < 0) return false;
        if (Direction == RuleDirection.MoneyOut && amount > 0) return false;

        var magnitude = Math.Abs(amount);
        if (MinAmount is decimal min && magnitude < min) return false;
        if (MaxAmount is decimal max && magnitude > max) return false;

        var haystack = (description ?? string.Empty).ToUpperInvariant();
        var needle = (MatchText ?? string.Empty).ToUpperInvariant();
        if (needle.Length == 0) return false;

        return MatchType switch
        {
            RuleMatch.Contains => haystack.Contains(needle),
            RuleMatch.StartsWith => haystack.StartsWith(needle),
            RuleMatch.Exact => haystack.Trim() == needle.Trim(),
            _ => false,
        };
    }
}
