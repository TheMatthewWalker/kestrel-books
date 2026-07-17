using System.Text.Json;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record ConnectionDetailsRequest(string? Vrn, string? Nino);
public record SubmitVatRequest(string PeriodKey, DateOnly From, DateOnly To, VatBoxes Boxes, bool Finalised);
public record ItsaQuarterRequest(string HmrcBusinessId, string TaxYear, DateOnly From, DateOnly To,
    decimal? TurnoverOverride, decimal? ConsolidatedExpensesOverride);

/// <summary>
/// Making Tax Digital. VAT uses the stable v1.0 organisations/vat API.
/// ITSA endpoints iterate quickly at HMRC — the paths below are grouped as
/// constants; verify them against the current versions on
/// developer.service.hmrc.gov.uk before going live (the sandbox returns
/// clear 404s if a path has moved).
/// </summary>
[ApiController]
[Authorize]
[Route("api/mtd")]
public class MtdController : ControllerBase
{
    private const string ItsaBusinessDetailsPath = "/individuals/business/details/{nino}/list";
    private const string ItsaObligationsPath = "/obligations/details/{nino}/income-and-expenditure";
    private const string ItsaQuarterlyPath = "/individuals/business/self-employment/{nino}/{bid}/period";

    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly HmrcService _hmrc;
    private readonly VatReturnService _vat;
    public MtdController(AppDbContext db, AccessService access, HmrcService hmrc, VatReturnService vat)
    {
        _db = db; _access = access; _hmrc = hmrc; _vat = vat;
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ---- Connection ----

    [HttpGet("businesses/{businessId:guid}/authorise-url")]
    public async Task<IActionResult> AuthoriseUrl(Guid businessId,
        [FromQuery] string scope = "read:vat write:vat read:self-assessment write:self-assessment")
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var url = _hmrc.BuildAuthoriseUrl(businessId, AccessService.UserId(User), scope);
        return Ok(new { url });
    }

    /// <summary>OAuth2 redirect target — hit by the phone's browser, so anonymous;
    /// the encrypted state carries and verifies the business identity.</summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<ContentResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        string message;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            message = $"Authorisation was not completed ({error ?? "missing code"}). Return to the app and try again.";
        }
        else
        {
            try
            {
                var (businessId, _) = _hmrc.UnprotectState(state);
                await _hmrc.ExchangeCodeAsync(businessId, code);
                message = "Connected to HMRC successfully. You can close this page and return to KestrelBooks.";
            }
            catch (Exception ex)
            {
                message = $"Connection failed: {ex.Message}";
            }
        }
        return Content($"<html><body style=\"font-family:sans-serif;padding:2rem\"><h2>KestrelBooks</h2><p>{message}</p></body></html>",
            "text/html");
    }

    [HttpGet("businesses/{businessId:guid}/status")]
    public async Task<IActionResult> Status(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var conn = await _db.HmrcConnections.FirstOrDefaultAsync(c => c.BusinessId == businessId);
        return Ok(new
        {
            configured = !string.IsNullOrEmpty(HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["Hmrc:ClientId"]),
            connected = conn != null,
            vrn = conn?.Vrn,
            nino = conn?.Nino,
            scope = conn?.Scope,
            connectedAtUtc = conn?.ConnectedAtUtc,
            deviceRegistered = conn?.DeviceInfoJson != null,
        });
    }

    /// <summary>Set the identifiers HMRC keys everything on (VRN for VAT, NINO for ITSA).</summary>
    [HttpPut("businesses/{businessId:guid}/details")]
    public async Task<IActionResult> SetDetails(Guid businessId, ConnectionDetailsRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var conn = await _db.HmrcConnections.FirstOrDefaultAsync(c => c.BusinessId == businessId);
        if (conn is null) return BadRequest(new { error = "Connect to HMRC first." });
        conn.Vrn = req.Vrn; conn.Nino = req.Nino;
        await _db.SaveChangesAsync();
        return Ok(new { saved = true });
    }

    /// <summary>The app registers device details here for the Gov-Client-* fraud prevention headers.</summary>
    [HttpPut("businesses/{businessId:guid}/device")]
    public async Task<IActionResult> RegisterDevice(Guid businessId, DeviceInfo device)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var conn = await _db.HmrcConnections.FirstOrDefaultAsync(c => c.BusinessId == businessId);
        if (conn is null) return BadRequest(new { error = "Connect to HMRC first." });
        conn.DeviceInfoJson = JsonSerializer.Serialize(device);
        await _db.SaveChangesAsync();
        return Ok(new { registered = true });
    }

    /// <summary>Proxies HMRC's fraud prevention header validation (sandbox only) so the header set can be checked before go-live.</summary>
    [HttpGet("businesses/{businessId:guid}/validate-fraud-headers")]
    public async Task<IActionResult> ValidateFraudHeaders(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var (status, body) = await _hmrc.SendAsync(businessId, HttpMethod.Get,
            "/test/fraud-prevention-headers/validate", null, ClientIp());
        return StatusCode(status == 0 ? 500 : 200, body);
    }

    // ---- VAT ----

    [HttpGet("businesses/{businessId:guid}/vat/obligations")]
    public async Task<IActionResult> VatObligations(Guid businessId, [FromQuery] string? status)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (code, body) = await _vat.GetObligationsAsync(businessId, status);
        return StatusCode(code, body);
    }

    /// <summary>Ledger-computed 9-box preview for a period — editable in the app before submission.</summary>
    [HttpGet("businesses/{businessId:guid}/vat/preview")]
    public async Task<IActionResult> VatPreview(Guid businessId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _vat.ComputeAsync(businessId, from, to));
    }

    [HttpPost("businesses/{businessId:guid}/vat/submit")]
    public async Task<IActionResult> VatSubmit(Guid businessId, SubmitVatRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        var submission = await _vat.SubmitAsync(businessId, req.PeriodKey, req.From, req.To,
            req.Boxes, req.Finalised, AccessService.UserId(User), ClientIp());
        return Ok(new { submission.Id, submission.FormBundleNumber, submission.ProcessingDate });
    }

    [HttpGet("businesses/{businessId:guid}/vat/submissions")]
    public async Task<IActionResult> VatSubmissions(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.VatSubmissions.Where(s => s.BusinessId == businessId)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new { s.Id, s.PeriodKey, s.PeriodFrom, s.PeriodTo, s.SubmittedAtUtc, s.FormBundleNumber })
            .ToListAsync());
    }

    // ---- ITSA (Income Tax Self Assessment) ----

    [HttpGet("businesses/{businessId:guid}/itsa/businesses")]
    public async Task<IActionResult> ItsaBusinesses(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var conn = await _hmrc.RequireConnectionAsync(businessId);
        if (string.IsNullOrEmpty(conn.Nino)) return BadRequest(new { error = "Set the NINO first." });
        var (code, body) = await _hmrc.SendAsync(businessId, HttpMethod.Get,
            ItsaBusinessDetailsPath.Replace("{nino}", conn.Nino), null, ClientIp());
        return StatusCode(code, body);
    }

    [HttpGet("businesses/{businessId:guid}/itsa/obligations")]
    public async Task<IActionResult> ItsaObligations(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var conn = await _hmrc.RequireConnectionAsync(businessId);
        if (string.IsNullOrEmpty(conn.Nino)) return BadRequest(new { error = "Set the NINO first." });
        var (code, body) = await _hmrc.SendAsync(businessId, HttpMethod.Get,
            ItsaObligationsPath.Replace("{nino}", conn.Nino), null, ClientIp());
        return StatusCode(code, body);
    }

    /// <summary>
    /// Quarterly update: turnover and consolidated expenses computed from the
    /// P&L for the quarter (overridable). Consolidated expenses are permitted
    /// below the VAT threshold; detailed SA103 category mapping is on the roadmap.
    /// </summary>
    [HttpPost("businesses/{businessId:guid}/itsa/quarterly")]
    public async Task<IActionResult> ItsaQuarterly(Guid businessId, ItsaQuarterRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        var conn = await _hmrc.RequireConnectionAsync(businessId);
        if (string.IsNullOrEmpty(conn.Nino)) return BadRequest(new { error = "Set the NINO first." });

        var lines = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status != JournalStatus.Draft
                        && l.JournalEntry.Date >= req.From && l.JournalEntry.Date <= req.To
                        && (l.Account.Type == AccountType.Income || l.Account.Type == AccountType.Expense))
            .Select(l => new { l.Account.Type, l.Debit, l.Credit })
            .ToListAsync();
        var turnover = req.TurnoverOverride
            ?? lines.Where(l => l.Type == AccountType.Income).Sum(l => l.Credit - l.Debit);
        var expenses = req.ConsolidatedExpensesOverride
            ?? lines.Where(l => l.Type == AccountType.Expense).Sum(l => l.Debit - l.Credit);

        var payload = new
        {
            periodFromDate = req.From.ToString("yyyy-MM-dd"),
            periodToDate = req.To.ToString("yyyy-MM-dd"),
            periodIncome = new { turnover = Math.Round(turnover, 2) },
            periodExpenses = new { consolidatedExpenses = Math.Round(expenses, 2) },
        };
        var path = ItsaQuarterlyPath.Replace("{nino}", conn.Nino).Replace("{bid}", req.HmrcBusinessId);
        var (code, body) = await _hmrc.SendAsync(businessId, HttpMethod.Post, path, payload, ClientIp());
        return StatusCode(code, body);
    }

    public record VatSchemeRequest(VatScheme Scheme, decimal FlatRatePercent);

    [HttpGet("businesses/{businessId:guid}/vat-scheme")]
    public async Task<IActionResult> GetVatScheme(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var b = await _db.Businesses.FirstAsync(x => x.Id == businessId);
        return Ok(new { scheme = b.VatScheme, flatRatePercent = b.FlatRatePercent });
    }

    /// <summary>Accountant+: the scheme changes how every subsequent return is computed.</summary>
    [HttpPut("businesses/{businessId:guid}/vat-scheme")]
    public async Task<IActionResult> SetVatScheme(Guid businessId, VatSchemeRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        if (req.Scheme == VatScheme.FlatRate && (req.FlatRatePercent <= 0 || req.FlatRatePercent >= 30))
            return BadRequest(new { error = "Flat rate percentage must be between 0 and 30 (check your trade sector rate on gov.uk)." });
        var b = await _db.Businesses.FirstAsync(x => x.Id == businessId);
        b.VatScheme = req.Scheme;
        b.FlatRatePercent = req.Scheme == VatScheme.FlatRate ? req.FlatRatePercent : 0;
        await _db.SaveChangesAsync();
        return Ok(new { scheme = b.VatScheme, flatRatePercent = b.FlatRatePercent });
    }
}
