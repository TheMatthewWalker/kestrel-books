namespace KestrelBooks.Api.Domain;

public enum PeriodEndKind
{
    /// <summary>Cost incurred but not yet invoiced — posted at period end, reversed at the start of the next.</summary>
    Accrual = 0,
    /// <summary>Cost paid in advance — held on the balance sheet and released to the P&L over N periods.</summary>
    Prepayment = 1,
    /// <summary>Income earned but not yet invoiced.</summary>
    AccruedIncome = 2,
    /// <summary>Income received in advance — held as a liability and released over N periods.</summary>
    DeferredIncome = 3,
}

public enum ScheduleStatus { Active = 0, Completed = 1, Cancelled = 2 }

/// <summary>
/// A period-end adjustment that the system carries forward for you.
///
/// Accruals and accrued income are the "post now, reverse next period" kind: the
/// whole amount hits the P&amp;L at the period end and is automatically reversed on
/// the first day of the next period, so the real invoice lands cleanly when it
/// arrives without double counting.
///
/// Prepayments and deferred income are the "spread it" kind: the amount sits on
/// the balance sheet and is released to the P&amp;L in equal monthly instalments
/// across the number of periods you specify.
///
/// Either way the point is the same — the accounts show the cost or income in the
/// period it belongs to, not the period the paperwork happened to arrive in.
/// </summary>
public class PeriodEndSchedule
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public PeriodEndKind Kind { get; set; }
    public string Description { get; set; } = "";
    public decimal TotalAmount { get; set; }

    /// <summary>P&L account the cost or income belongs to.</summary>
    public Guid PandLAccountId { get; set; }
    /// <summary>Balance sheet account holding the other side (accruals, prepayments, deferred income).</summary>
    public Guid BalanceSheetAccountId { get; set; }

    /// <summary>Period this adjustment is made at (accruals) or starts releasing from (prepayments).</summary>
    public DateOnly StartDate { get; set; }
    /// <summary>Spread kinds only: how many monthly instalments the amount is released over.</summary>
    public int Periods { get; set; } = 1;
    public int PeriodsReleased { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Active;

    public DateOnly? NextRunDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public List<PeriodEndPosting> Postings { get; set; } = new();

    public bool IsSpread => Kind is PeriodEndKind.Prepayment or PeriodEndKind.DeferredIncome;
    public decimal MonthlyAmount => Periods <= 0
        ? TotalAmount
        : decimal.Round(TotalAmount / Periods, 2, MidpointRounding.AwayFromZero);
}

/// <summary>One journal produced by a schedule — the audit trail of what was released when.</summary>
public class PeriodEndPosting
{
    public Guid Id { get; set; }
    public Guid PeriodEndScheduleId { get; set; }
    public Guid JournalEntryId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public bool IsReversal { get; set; }
}
