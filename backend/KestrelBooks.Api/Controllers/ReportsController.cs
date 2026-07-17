using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly AccessService _access;
    private readonly AgedReportService _aged;
    public ReportsController(ReportService reports, AccessService access, AgedReportService aged)
    {
        _reports = reports; _access = access; _aged = aged;
    }

    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _reports.TrialBalanceAsync(businessId, asOf ?? DateOnly.FromDateTime(DateTime.Today)));
    }

    [HttpGet("profit-and-loss")]
    public async Task<IActionResult> ProfitAndLoss(Guid businessId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _reports.ProfitAndLossAsync(businessId, from, to));
    }

    [HttpGet("balance-sheet")]
    public async Task<IActionResult> BalanceSheet(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _reports.BalanceSheetAsync(businessId, asOf ?? DateOnly.FromDateTime(DateTime.Today)));
    }

    [HttpGet("cash-flow")]
    public async Task<IActionResult> CashFlow(Guid businessId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _reports.CashFlowAsync(businessId, from, to));
    }

    [HttpGet("aged-debtors")]
    public async Task<IActionResult> AgedDebtors(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _aged.AgedDebtorsAsync(businessId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpGet("aged-creditors")]
    public async Task<IActionResult> AgedCreditors(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _aged.AgedCreditorsAsync(businessId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [HttpGet("customer-statement/{customerId:guid}")]
    public async Task<IActionResult> CustomerStatement(Guid businessId, Guid customerId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _aged.CustomerStatementAsync(businessId, customerId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)));
    }
}
