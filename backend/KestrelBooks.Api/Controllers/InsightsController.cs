using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record RequestRecordsRequest(string ToEmail, DateOnly From, DateOnly To, decimal? Threshold);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/insights")]
public class InsightsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly RecordsGapService _gaps;
    private readonly CashFlowForecastService _forecast;
    public InsightsController(AppDbContext db, AccessService access,
        RecordsGapService gaps, CashFlowForecastService forecast)
    {
        _db = db; _access = access; _gaps = gaps; _forecast = forecast;
    }

    [HttpGet("records-gap")]
    public async Task<IActionResult> RecordsGap(Guid businessId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] decimal? threshold)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _gaps.FindAsync(businessId, from, to, threshold ?? 50m));
    }

    /// <summary>Emails the client the itemised list of exactly what is missing.</summary>
    [HttpPost("records-gap/request")]
    public async Task<IActionResult> RequestRecords(Guid businessId, RequestRecordsRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var gap = await _gaps.FindAsync(businessId, req.From, req.To, req.Threshold ?? 50m);
        if (gap.Count == 0) return Ok(new { requested = 0, message = "Nothing is missing." });
        var count = await _gaps.RequestAsync(businessId, req.ToEmail, gap);
        return Ok(new { requested = count, sentTo = req.ToEmail });
    }

    [HttpGet("cash-flow-forecast")]
    public async Task<IActionResult> Forecast(Guid businessId, [FromQuery] int weeks = 13)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _forecast.BuildAsync(businessId,
            DateOnly.FromDateTime(DateTime.UtcNow), weeks < 1 || weeks > 52 ? 13 : weeks));
    }
}
