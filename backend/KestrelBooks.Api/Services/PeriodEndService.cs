using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Accruals, prepayments, accrued and deferred income — the adjustments that put
/// costs and income in the period they belong to rather than the period the
/// paperwork arrived in. Without these there is no meaningful month end.
/// </summary>
public class PeriodEndService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    private readonly TenantProvider _tenant;
    public PeriodEndService(AppDbContext db, PostingService posting, TenantProvider tenant)
    {
        _db = db; _posting = posting; _tenant = tenant;
    }

    public async Task<PeriodEndSchedule> CreateAsync(Guid businessId, Guid userId, PeriodEndKind kind,
        string description, decimal totalAmount, Guid pandLAccountId, Guid balanceSheetAccountId,
        DateOnly startDate, int periods)
    {
        if (totalAmount <= 0) throw new InvalidOperationException("Amount must be positive.");
        var spread = kind is PeriodEndKind.Prepayment or PeriodEndKind.DeferredIncome;
        if (spread && periods < 1)
            throw new InvalidOperationException("Spread adjustments need at least one period.");

        var schedule = new PeriodEndSchedule
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Kind = kind, Description = description,
            TotalAmount = totalAmount, PandLAccountId = pandLAccountId,
            BalanceSheetAccountId = balanceSheetAccountId, StartDate = startDate,
            Periods = spread ? periods : 1, NextRunDate = startDate, CreatedBy = userId,
        };
        _db.PeriodEndSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return schedule;
    }

    /// <summary>
    /// Posts everything a schedule owes up to <paramref name="upTo"/>.
    ///
    /// Accrual kinds post the full amount at the period end and immediately post
    /// the mirror-image reversal dated the first day of the following month — so
    /// when the real invoice arrives next period it lands against a clean account
    /// instead of double counting.
    ///
    /// Spread kinds release one monthly instalment at a time, with the final
    /// instalment taking any rounding remainder so the schedule always clears to
    /// exactly the original amount.
    /// </summary>
    public async Task<List<Guid>> RunAsync(Guid businessId, Guid scheduleId, DateOnly upTo, Guid userId)
    {
        var s = await _db.PeriodEndSchedules
            .FirstOrDefaultAsync(x => x.Id == scheduleId && x.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Schedule not found.");
        var created = new List<Guid>();
        if (s.Status != ScheduleStatus.Active) return created;

        if (!s.IsSpread)
        {
            if (s.PeriodsReleased > 0 || s.StartDate > upTo) return created;

            var (drAccrual, crAccrual) = s.Kind == PeriodEndKind.Accrual
                ? (s.PandLAccountId, s.BalanceSheetAccountId)   // Dr expense, Cr accruals
                : (s.BalanceSheetAccountId, s.PandLAccountId);  // accrued income: Dr debtor-ish, Cr income

            var main = await _posting.CreateDraftAsync(businessId, userId, s.StartDate,
                "PE-ACCR", $"{Label(s.Kind)}: {s.Description}", SourceType.Accrual, s.Id,
                new[]
                {
                    new DraftLine(drAccrual, s.TotalAmount, 0, s.Description),
                    new DraftLine(crAccrual, 0, s.TotalAmount, s.Description),
                });
            await _posting.PostAsync(businessId, main.Id, userId);
            created.Add(main.Id);
            _db.PeriodEndPostings.Add(new PeriodEndPosting
            {
                Id = Guid.NewGuid(), PeriodEndScheduleId = s.Id, JournalEntryId = main.Id,
                Date = s.StartDate, Amount = s.TotalAmount, IsReversal = false,
            });

            // The reversal, dated the first day of the next month.
            var reversalDate = new DateOnly(s.StartDate.Year, s.StartDate.Month, 1).AddMonths(1);
            var reversal = await _posting.CreateDraftAsync(businessId, userId, reversalDate,
                "PE-REV", $"Reversal of {Label(s.Kind).ToLowerInvariant()}: {s.Description}",
                SourceType.Accrual, s.Id,
                new[]
                {
                    new DraftLine(crAccrual, s.TotalAmount, 0, $"Reverse — {s.Description}"),
                    new DraftLine(drAccrual, 0, s.TotalAmount, $"Reverse — {s.Description}"),
                });
            await _posting.PostAsync(businessId, reversal.Id, userId);
            created.Add(reversal.Id);
            _db.PeriodEndPostings.Add(new PeriodEndPosting
            {
                Id = Guid.NewGuid(), PeriodEndScheduleId = s.Id, JournalEntryId = reversal.Id,
                Date = reversalDate, Amount = s.TotalAmount, IsReversal = true,
            });

            s.PeriodsReleased = 1;
            s.Status = ScheduleStatus.Completed;
            s.NextRunDate = null;
            await _db.SaveChangesAsync();
            return created;
        }

        // Spread kinds: one instalment per month while due.
        while (s.Status == ScheduleStatus.Active
               && s.PeriodsReleased < s.Periods
               && s.NextRunDate is DateOnly due && due <= upTo)
        {
            var isFinal = s.PeriodsReleased == s.Periods - 1;
            var released = s.MonthlyAmount * s.PeriodsReleased;
            var amount = isFinal ? s.TotalAmount - released : s.MonthlyAmount;

            var (dr, cr) = s.Kind == PeriodEndKind.Prepayment
                ? (s.PandLAccountId, s.BalanceSheetAccountId)   // release prepaid cost into the P&L
                : (s.BalanceSheetAccountId, s.PandLAccountId);  // release deferred income into the P&L

            var journal = await _posting.CreateDraftAsync(businessId, userId, due,
                "PE-REL", $"{Label(s.Kind)} release {s.PeriodsReleased + 1}/{s.Periods}: {s.Description}",
                s.Kind == PeriodEndKind.Prepayment ? SourceType.Prepayment : SourceType.Accrual, s.Id,
                new[]
                {
                    new DraftLine(dr, amount, 0, s.Description),
                    new DraftLine(cr, 0, amount, s.Description),
                });
            await _posting.PostAsync(businessId, journal.Id, userId);
            created.Add(journal.Id);
            _db.PeriodEndPostings.Add(new PeriodEndPosting
            {
                Id = Guid.NewGuid(), PeriodEndScheduleId = s.Id, JournalEntryId = journal.Id,
                Date = due, Amount = amount, IsReversal = false,
            });

            s.PeriodsReleased++;
            // Each release date is computed from the start rather than by adding a
            // month to the last one. A schedule beginning 31 January must release
            // on 28 Feb, 31 Mar, 30 Apr — stepping month-by-month would clamp to
            // the 28th in February and then stay there for the rest of the year,
            // quietly moving every remaining instalment off the month end.
            s.NextRunDate = s.StartDate.AddMonths(s.PeriodsReleased);
            if (s.PeriodsReleased >= s.Periods)
            {
                s.Status = ScheduleStatus.Completed;
                s.NextRunDate = null;
            }
            await _db.SaveChangesAsync();
        }
        return created;
    }

    public async Task CancelAsync(Guid businessId, Guid scheduleId)
    {
        var s = await _db.PeriodEndSchedules
            .FirstOrDefaultAsync(x => x.Id == scheduleId && x.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Schedule not found.");
        s.Status = ScheduleStatus.Cancelled;
        s.NextRunDate = null;
        await _db.SaveChangesAsync();
    }

    /// <summary>Sweeps every business's due schedules — used by the month-end background run.</summary>
    public async Task<int> RunAllDueAsync(DateOnly upTo, Guid systemUserId)
    {
        var due = await _db.PeriodEndSchedules.IgnoreQueryFilters()
            .Where(s => s.Status == ScheduleStatus.Active && s.NextRunDate != null && s.NextRunDate <= upTo)
            .Select(s => new { s.Id, s.BusinessId })
            .ToListAsync();

        var total = 0;
        foreach (var s in due)
        {
            _tenant.Set(s.BusinessId, BusinessRole.Owner);
            total += (await RunAsync(s.BusinessId, s.Id, upTo, systemUserId)).Count;
        }
        return total;
    }

    private static string Label(PeriodEndKind kind) => kind switch
    {
        PeriodEndKind.Accrual => "Accrual",
        PeriodEndKind.Prepayment => "Prepayment",
        PeriodEndKind.AccruedIncome => "Accrued income",
        PeriodEndKind.DeferredIncome => "Deferred income",
        _ => "Adjustment",
    };
}
