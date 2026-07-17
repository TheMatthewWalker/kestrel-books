using KestrelBooks.Api.Data;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly AccessService _access;
    private readonly AgedReportService _aged;
    private readonly PdfService _pdf;
    private readonly IEmailSender _email;
    private readonly Data.AppDbContext _db;
    public ReportsController(ReportService reports, AccessService access, AgedReportService aged, PdfService pdf, IEmailSender email, AppDbContext db)
    {
        _reports = reports; _access = access; _aged = aged; _pdf = pdf; _email = email; _db = db;
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

    [HttpGet("customer-statement/{customerId:guid}/pdf")]
    public async Task<IActionResult> CustomerStatementPdf(Guid businessId, Guid customerId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var statement = await _aged.CustomerStatementAsync(businessId, customerId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return File(_pdf.StatementPdf(statement), "application/pdf",
            $"statement-{statement.ContactName.Replace(' ', '-')}-{statement.AsOf:yyyyMMdd}.pdf");
    }

    /// <summary>Emails the open-item statement to the customer — the weekly credit-control chase.</summary>
    [HttpPost("customer-statement/{customerId:guid}/email")]
    public async Task<IActionResult> CustomerStatementEmail(Guid businessId, Guid customerId)
    {
        await _access.EnsureAccessAsync(User, businessId, Domain.BusinessRole.Bookkeeper);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.BusinessId == businessId);
        if (customer is null) return NotFound();
        if (string.IsNullOrEmpty(customer.Email))
            return BadRequest(new { error = "The customer has no email address." });
        var statement = await _aged.CustomerStatementAsync(businessId, customerId,
            DateOnly.FromDateTime(DateTime.UtcNow));
        var pdf = _pdf.StatementPdf(statement);
        await _email.SendAsync(customer.Email,
            $"Statement of account from {statement.BusinessName}",
            $"Please find attached your statement of account as at {statement.AsOf:dd MMM yyyy}. " +
            $"Total due: £{statement.TotalDue:N2}.",
            new[] { new EmailAttachment("statement.pdf", pdf, "application/pdf") });
        return Ok(new { sentTo = customer.Email, totalDue = statement.TotalDue });
    }
}
