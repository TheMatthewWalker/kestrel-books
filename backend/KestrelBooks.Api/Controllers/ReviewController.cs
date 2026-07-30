using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/review")]
public class ReviewController : ControllerBase
{
    private readonly AccessService _access;
    private readonly ReviewAssistantService _review;
    private readonly VatPreflightService _preflight;
    public ReviewController(AccessService access, ReviewAssistantService review,
        VatPreflightService preflight)
    {
        _access = access; _review = review; _preflight = preflight;
    }

    /// <summary>The pre-sign-off checklist, run over the ledger.</summary>
    [HttpGet]
    public async Task<IActionResult> Review(Guid businessId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _review.ReviewAsync(businessId, from, to));
    }

    /// <summary>Sanity checks a VAT return against this client's history before filing.</summary>
    [HttpGet("vat-preflight")]
    public async Task<IActionResult> VatPreflight(Guid businessId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _preflight.CheckAsync(businessId, from, to));
    }
}
