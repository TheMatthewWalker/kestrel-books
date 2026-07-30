using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record BudgetRequest(string Name, DateOnly StartMonth, int Months);
public record BudgetLineRequest(Guid AccountId, Guid? TrackingOptionId, DateOnly Month, decimal Amount);
public record SeedRequest(DateOnly SourceFrom, DateOnly SourceTo, decimal UpliftPercent);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/budgets")]
public class BudgetsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly BudgetService _budgets;
    public BudgetsController(AppDbContext db, AccessService access, BudgetService budgets)
    {
        _db = db; _access = access; _budgets = budgets;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Budgets.Where(b => b.BusinessId == businessId)
            .OrderByDescending(b => b.StartMonth)
            .Select(b => new
            {
                b.Id, b.Name, b.StartMonth, b.Months, b.IsActive,
                LineCount = _db.BudgetLines.Count(l => l.BudgetId == b.Id),
            })
            .ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, BudgetRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var budget = new Budget
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = req.Name,
            StartMonth = new DateOnly(req.StartMonth.Year, req.StartMonth.Month, 1),
            Months = req.Months < 1 ? 12 : req.Months,
        };
        _db.Budgets.Add(budget);
        await _db.SaveChangesAsync();
        return Ok(new { budget.Id });
    }

    [HttpGet("{id:guid}/lines")]
    public async Task<IActionResult> Lines(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.BudgetLines.Where(l => l.BudgetId == id && l.BusinessId == businessId)
            .OrderBy(l => l.Month)
            .Select(l => new
            {
                l.Id, l.AccountId, l.TrackingOptionId, l.Month, l.Amount,
                Code = _db.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.Code).FirstOrDefault(),
                Name = _db.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.Name).FirstOrDefault(),
            })
            .ToListAsync());
    }

    /// <summary>Replaces the figures for one account across the budget's months.</summary>
    [HttpPut("{id:guid}/lines")]
    public async Task<IActionResult> SetLines(Guid businessId, Guid id, List<BudgetLineRequest> lines)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId);
        if (budget is null) return NotFound();

        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var existing = await _db.BudgetLines
            .Where(l => l.BudgetId == id && accountIds.Contains(l.AccountId)).ToListAsync();
        _db.BudgetLines.RemoveRange(existing);

        foreach (var l in lines.Where(l => l.Amount != 0))
            _db.BudgetLines.Add(new BudgetLine
            {
                Id = Guid.NewGuid(), BudgetId = id, BusinessId = businessId,
                AccountId = l.AccountId, TrackingOptionId = l.TrackingOptionId,
                Month = new DateOnly(l.Month.Year, l.Month.Month, 1), Amount = l.Amount,
            });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/seed-from-actuals")]
    public async Task<IActionResult> Seed(Guid businessId, Guid id, SeedRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var created = await _budgets.SeedFromActualsAsync(businessId, id,
            req.SourceFrom, req.SourceTo, req.UpliftPercent);
        return Ok(new { linesCreated = created });
    }

    [HttpGet("{id:guid}/variance")]
    public async Task<IActionResult> Variance(Guid businessId, Guid id,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? trackingOptionId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _budgets.VarianceAsync(businessId, id, from, to, trackingOptionId));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId);
        if (budget is null) return NotFound();
        _db.BudgetLines.RemoveRange(_db.BudgetLines.Where(l => l.BudgetId == id));
        _db.Budgets.Remove(budget);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
