using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record StageRequest(string Name, int DaysOverdue, string Subject, string Body,
    bool AttachStatement, bool Enabled);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/credit-control")]
public class CreditControlController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly CreditControlService _credit;
    public CreditControlController(AppDbContext db, AccessService access, CreditControlService credit)
    {
        _db = db; _access = access; _credit = credit;
    }

    [HttpGet("stages")]
    public async Task<IActionResult> Stages(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.CreditControlStages.Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.DaysOverdue).ToListAsync());
    }

    /// <summary>Creates the default three-rung ladder for a business that has none.</summary>
    [HttpPost("stages/seed-defaults")]
    public async Task<IActionResult> SeedDefaults(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (await _db.CreditControlStages.AnyAsync(s => s.BusinessId == businessId))
            return BadRequest(new { error = "This client already has a chase ladder." });
        _db.CreditControlStages.AddRange(CreditControlService.DefaultLadder(businessId));
        await _db.SaveChangesAsync();
        return Ok(new { seeded = true });
    }

    [HttpPost("stages")]
    public async Task<IActionResult> CreateStage(Guid businessId, StageRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var stage = new CreditControlStage
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = req.Name,
            DaysOverdue = req.DaysOverdue, Subject = req.Subject, Body = req.Body,
            AttachStatement = req.AttachStatement, Enabled = req.Enabled,
        };
        _db.CreditControlStages.Add(stage);
        await _db.SaveChangesAsync();
        return Ok(new { stage.Id });
    }

    [HttpPut("stages/{id:guid}")]
    public async Task<IActionResult> UpdateStage(Guid businessId, Guid id, StageRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var stage = await _db.CreditControlStages
            .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId);
        if (stage is null) return NotFound();
        stage.Name = req.Name; stage.DaysOverdue = req.DaysOverdue;
        stage.Subject = req.Subject; stage.Body = req.Body;
        stage.AttachStatement = req.AttachStatement; stage.Enabled = req.Enabled;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("stages/{id:guid}")]
    public async Task<IActionResult> DeleteStage(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var stage = await _db.CreditControlStages
            .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId);
        if (stage is null) return NotFound();
        _db.CreditControlStages.Remove(stage);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Shows exactly what would be sent, without sending anything.</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _credit.RunAsync(businessId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), send: false));
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(Guid businessId, [FromQuery] DateOnly? asOf)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        return Ok(await _credit.RunAsync(businessId,
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), send: true));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.CreditControlLogs.Where(l => l.BusinessId == businessId)
            .OrderByDescending(l => l.SentAtUtc).Take(300)
            .Select(l => new
            {
                l.Id, l.StageName, l.DaysOverdueAtSend, l.OutstandingAtSend, l.SentTo, l.SentAtUtc,
                InvoiceNumber = _db.SalesInvoices.Where(i => i.Id == l.SalesInvoiceId)
                    .Select(i => i.Number).FirstOrDefault(),
                CustomerName = _db.Customers.Where(c => c.Id == l.CustomerId)
                    .Select(c => c.Name).FirstOrDefault(),
            })
            .ToListAsync());
    }
}
