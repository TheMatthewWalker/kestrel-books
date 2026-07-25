using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/data")]
public class DataController : ControllerBase
{
    private readonly AccessService _access;
    private readonly DataExportService _export;
    public DataController(AccessService access, DataExportService export)
    {
        _access = access; _export = export;
    }

    /// <summary>Everything, as a zip of CSVs. Owner-only — this is the whole book.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Owner);
        var bytes = await _export.ExportAsync(businessId);
        return File(bytes, "application/zip",
            $"kestrelbooks-export-{DateTime.UtcNow:yyyyMMdd}.zip");
    }
}
