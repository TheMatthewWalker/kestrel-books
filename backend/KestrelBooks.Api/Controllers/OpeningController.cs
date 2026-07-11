using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

public record ImportTbRequest(DateOnly ConversionDate, List<TbLine> Lines);
public record OpeningStockRequest(DateOnly ConversionDate, List<OpeningStockLine> Lines);

/// <summary>
/// Client conversion (opening balances). Workflow:
///   1. parse-csv (optional) → review matched/unmatched rows
///   2. trial-balance → one posted OPENING journal, dated conversion − 1 day
///   3. open sales/purchase invoices (no journals — totals live in the TB controls)
///   4. stock (no journal — value lives in the TB stock lines)
///   5. status → reconcile entered invoices against the TB control figures
/// </summary>
[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/opening")]
public class OpeningController : ControllerBase
{
    private readonly AccessService _access;
    private readonly OpeningBalanceService _opening;
    private readonly StockService _stock;
    public OpeningController(AccessService access, OpeningBalanceService opening, StockService stock)
    {
        _access = access; _opening = opening; _stock = stock;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _opening.StatusAsync(businessId));
    }

    [HttpPost("trial-balance/parse-csv")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ParseCsv(Guid businessId, IFormFile file)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file received." });
        using var reader = new StreamReader(file.OpenReadStream());
        return Ok(await _opening.ParseCsvAsync(businessId, await reader.ReadToEndAsync()));
    }

    [HttpPost("trial-balance")]
    public async Task<IActionResult> ImportTb(Guid businessId, ImportTbRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _opening.ImportTrialBalanceAsync(
            businessId, req.ConversionDate, req.Lines, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    [HttpPost("sales-invoices")]
    public async Task<IActionResult> OpenSales(Guid businessId, OpeningInvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        return Ok(new { id = await _opening.AddOpeningSalesInvoiceAsync(businessId, req) });
    }

    [HttpPost("purchase-invoices")]
    public async Task<IActionResult> OpenPurchases(Guid businessId, OpeningInvoiceRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        return Ok(new { id = await _opening.AddOpeningPurchaseInvoiceAsync(businessId, req) });
    }

    [HttpPost("stock")]
    public async Task<IActionResult> OpenStock(Guid businessId, OpeningStockRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        await _opening.SetOpeningStockAsync(businessId, req.ConversionDate, req.Lines, _stock);
        return Ok(new { set = req.Lines.Count });
    }
}
