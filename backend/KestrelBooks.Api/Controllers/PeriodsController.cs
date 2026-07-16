using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

public record LockRequest(DateOnly? Through);
public record CloseYearRequest(DateOnly YearEnd);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/periods")]
public class PeriodsController : ControllerBase
{
    private readonly AccessService _access;
    private readonly PeriodService _periods;
    public PeriodsController(AccessService access, PeriodService periods)
    {
        _access = access; _periods = periods;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _periods.StatusAsync(businessId));
    }

    /// <summary>Sets (or clears, with null) the lock date. Accountant+ — closing periods is their job.</summary>
    [HttpPut("lock")]
    public async Task<IActionResult> SetLock(Guid businessId, LockRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        await _periods.SetLockAsync(businessId, req.Through);
        return Ok(new { lockedThrough = req.Through });
    }

    [HttpPost("close-year")]
    public async Task<IActionResult> CloseYear(Guid businessId, CloseYearRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        var journal = await _periods.CloseYearAsync(businessId, req.YearEnd, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number, lockedThrough = req.YearEnd });
    }
}
