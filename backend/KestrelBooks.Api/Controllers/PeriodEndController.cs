using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record PeriodEndRequest(PeriodEndKind Kind, string Description, decimal TotalAmount,
    Guid PandLAccountId, Guid BalanceSheetAccountId, DateOnly StartDate, int Periods);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/period-end")]
public class PeriodEndController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly PeriodEndService _periodEnd;
    public PeriodEndController(AppDbContext db, AccessService access, PeriodEndService periodEnd)
    {
        _db = db; _access = access; _periodEnd = periodEnd;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var schedules = await _db.PeriodEndSchedules
            .Where(s => s.BusinessId == businessId)
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();

        // Postings are read separately rather than through the parent's collection:
        // the schedule is tenant-filtered and its children are not, so keeping the
        // two apart avoids EF fixing up a filtered principal.
        var scheduleIds = schedules.Select(s => s.Id).ToList();
        var postings = await _db.PeriodEndPostings
            .Where(p => scheduleIds.Contains(p.PeriodEndScheduleId))
            .Select(p => new { p.PeriodEndScheduleId, p.Amount, p.IsReversal })
            .ToListAsync();
        return Ok(schedules.Select(s => new
        {
            s.Id, s.Kind, s.Description, s.TotalAmount, s.StartDate, s.Periods,
            s.PeriodsReleased, s.Status, s.NextRunDate,
            s.IsSpread, s.MonthlyAmount,
            Released = postings.Where(p => p.PeriodEndScheduleId == s.Id && !p.IsReversal).Sum(p => p.Amount),
            Remaining = s.TotalAmount
                        - postings.Where(p => p.PeriodEndScheduleId == s.Id && !p.IsReversal).Sum(p => p.Amount),
            PostingCount = postings.Count(p => p.PeriodEndScheduleId == s.Id),
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, PeriodEndRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var s = await _periodEnd.CreateAsync(businessId, AccessService.UserId(User), req.Kind,
            req.Description, req.TotalAmount, req.PandLAccountId, req.BalanceSheetAccountId,
            req.StartDate, req.Periods);
        return Ok(new { s.Id });
    }

    /// <summary>Posts everything this schedule owes up to the given date (defaults to today).</summary>
    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(Guid businessId, Guid id, [FromQuery] DateOnly? upTo)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var created = await _periodEnd.RunAsync(businessId, id,
            upTo ?? DateOnly.FromDateTime(DateTime.UtcNow), AccessService.UserId(User));
        return Ok(new { posted = created.Count });
    }

    [HttpPost("run-all")]
    public async Task<IActionResult> RunAll(Guid businessId, [FromQuery] DateOnly? upTo)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var target = upTo ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var ids = await _db.PeriodEndSchedules
            .Where(s => s.BusinessId == businessId && s.Status == ScheduleStatus.Active
                        && s.NextRunDate != null && s.NextRunDate <= target)
            .Select(s => s.Id).ToListAsync();
        var total = 0;
        foreach (var id in ids)
            total += (await _periodEnd.RunAsync(businessId, id, target, AccessService.UserId(User))).Count;
        return Ok(new { schedules = ids.Count, posted = total });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        await _periodEnd.CancelAsync(businessId, id);
        return NoContent();
    }
}
