namespace KestrelBooks.Api.Domain;

/// <summary>
/// A budget is a set of monthly expectations per account, which is the only
/// shape that supports the question a management pack exists to answer: not
/// "what did we spend?" but "what did we spend against what we said we would?"
///
/// Stored as one row per account per month rather than an annual figure divided
/// by twelve, because real budgets are lumpy — insurance renews once, the
/// Christmas stock lands in November, the audit fee arrives in one hit.
/// </summary>
public class Budget
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>First month the budget covers; twelve monthly periods run from here.</summary>
    public DateOnly StartMonth { get; set; }
    public int Months { get; set; } = 12;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<BudgetLine> Lines { get; set; } = new();
}

public class BudgetLine
{
    public Guid Id { get; set; }
    public Guid BudgetId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid AccountId { get; set; }
    /// <summary>Optional: budget a department or project separately.</summary>
    public Guid? TrackingOptionId { get; set; }
    /// <summary>First day of the month this figure applies to.</summary>
    public DateOnly Month { get; set; }
    /// <summary>Signed the natural way round: income positive, expense positive.</summary>
    public decimal Amount { get; set; }
}
