using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

/// <summary>
/// Making Tax Digital scaffold (HMRC sandbox).
///
/// HMRC APIs use OAuth 2.0 authorisation code grant. Flow:
///  1. GET /api/mtd/authorise-url  → redirect the user to HMRC to grant access
///  2. HMRC redirects back to Hmrc:RedirectUri with ?code=...
///  3. Exchange the code for tokens at /oauth/token, store per business
///  4. Call MTD endpoints (VAT, Income Tax Self Assessment) with the access token
///     plus the mandatory Gov-Client-* fraud prevention headers.
///
/// Register the app at https://developer.service.hmrc.gov.uk to obtain
/// ClientId / ClientSecret, then populate the Hmrc section in appsettings.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mtd")]
public class MtdController : ControllerBase
{
    private readonly IConfiguration _config;
    public MtdController(IConfiguration config) => _config = config;

    [HttpGet("authorise-url")]
    public IActionResult AuthoriseUrl([FromQuery] string scope = "read:self-assessment write:self-assessment")
    {
        var clientId = _config["Hmrc:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            return BadRequest(new { error = "HMRC ClientId not configured. Register at developer.service.hmrc.gov.uk first." });

        var url = $"{_config["Hmrc:BaseUrl"]}/oauth/authorize" +
                  $"?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
                  $"&scope={Uri.EscapeDataString(scope)}" +
                  $"&redirect_uri={Uri.EscapeDataString(_config["Hmrc:RedirectUri"]!)}";
        return Ok(new { url });
    }

    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        configured = !string.IsNullOrEmpty(_config["Hmrc:ClientId"]),
        environment = _config["Hmrc:BaseUrl"],
        note = "Token exchange, fraud prevention headers and ITSA submission endpoints are on the roadmap — see docs/ROADMAP.md."
    });
}
