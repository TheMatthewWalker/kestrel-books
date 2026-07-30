using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/payroll")]
public class PayrollController : ControllerBase
{
    private readonly AccessService _access;
    private readonly PayrollImportService _payroll;
    public PayrollController(AccessService access, PayrollImportService payroll)
    {
        _access = access; _payroll = payroll;
    }

    /// <summary>Parses the file and reports what would post, without posting.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(Guid businessId, IFormFile file, [FromForm] DateOnly date)
    {
        await _access.EnsureAccessAsync(User, businessId);
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file received." });
        using var stream = file.OpenReadStream();
        return Ok(await _payroll.ParseAsync(businessId, stream, date));
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(Guid businessId, IFormFile file,
        [FromForm] DateOnly date, [FromForm] string? reference)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file received." });
        using var stream = file.OpenReadStream();
        var journal = await _payroll.ImportAsync(businessId, stream, date,
            reference ?? $"PAY-{date:yyyyMM}", AccessService.UserId(User));
        return Ok(new { journalNumber = journal.Number, journalId = journal.Id });
    }
}
