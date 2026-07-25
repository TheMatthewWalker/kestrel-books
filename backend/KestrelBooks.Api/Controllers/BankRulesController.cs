using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record BankRuleRequest(string Name, Guid? BankAccountId, RuleMatch MatchType, string MatchText,
    RuleDirection Direction, decimal? MinAmount, decimal? MaxAmount, Guid AccountId,
    VatRate VatRate, Guid? VendorId, Guid? CustomerId, bool AutoPost, int Priority);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/bank-rules")]
public class BankRulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly BankRuleService _rules;
    public BankRulesController(AppDbContext db, AccessService access, BankRuleService rules)
    {
        _db = db; _access = access; _rules = rules;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.BankRules.Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.Priority).ThenBy(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id, r.Name, r.MatchType, r.MatchText, r.Direction, r.MinAmount, r.MaxAmount,
                r.AccountId, r.VatRate, r.AutoPost, r.Enabled, r.Priority,
                r.TimesApplied, r.LastAppliedUtc, r.BankAccountId,
            })
            .ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, BankRuleRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (string.IsNullOrWhiteSpace(req.MatchText))
            return BadRequest(new { error = "A rule needs something to match on." });
        var rule = new BankRule
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = req.Name,
            BankAccountId = req.BankAccountId, MatchType = req.MatchType, MatchText = req.MatchText,
            Direction = req.Direction, MinAmount = req.MinAmount, MaxAmount = req.MaxAmount,
            AccountId = req.AccountId, VatRate = req.VatRate, VendorId = req.VendorId,
            CustomerId = req.CustomerId, AutoPost = req.AutoPost, Priority = req.Priority,
        };
        _db.BankRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(new { rule.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid id, BankRuleRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var rule = await _db.BankRules.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == businessId);
        if (rule is null) return NotFound();
        rule.Name = req.Name; rule.BankAccountId = req.BankAccountId;
        rule.MatchType = req.MatchType; rule.MatchText = req.MatchText;
        rule.Direction = req.Direction; rule.MinAmount = req.MinAmount; rule.MaxAmount = req.MaxAmount;
        rule.AccountId = req.AccountId; rule.VatRate = req.VatRate;
        rule.VendorId = req.VendorId; rule.CustomerId = req.CustomerId;
        rule.AutoPost = req.AutoPost; rule.Priority = req.Priority;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid businessId, Guid id, [FromQuery] bool enabled)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var rule = await _db.BankRules.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == businessId);
        if (rule is null) return NotFound();
        rule.Enabled = enabled;
        await _db.SaveChangesAsync();
        return Ok(new { rule.Enabled });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var rule = await _db.BankRules.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == businessId);
        if (rule is null) return NotFound();
        _db.BankRules.Remove(rule);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Runs every enabled rule over the unmatched lines on one bank account.</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(Guid businessId, [FromQuery] Guid bankAccountId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var result = await _rules.ApplyAsync(businessId, bankAccountId, AccessService.UserId(User));
        return Ok(result);
    }

    /// <summary>Proposes match text from a description the user is looking at.</summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(Guid businessId, [FromQuery] string description)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(new { matchText = BankRuleService.SuggestMatchText(description) });
    }
}
