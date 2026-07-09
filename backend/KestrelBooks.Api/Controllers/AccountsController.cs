using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record AccountRequest(string Code, string Name, AccountType Type, string? SubType, bool IsBank);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    public AccountsController(AppDbContext db, AccessService access) { _db = db; _access = access; }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Accounts
            .Where(a => a.BusinessId == businessId && !a.Archived)
            .OrderBy(a => a.Code)
            .Select(a => new { a.Id, a.Code, a.Name, a.Type, a.SubType, a.IsBank, a.SystemTag })
            .ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, AccountRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        if (await _db.Accounts.AnyAsync(a => a.BusinessId == businessId && a.Code == req.Code))
            return BadRequest(new { error = $"Account code {req.Code} already exists." });
        var acc = new Account
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Code = req.Code,
            Name = req.Name, Type = req.Type, SubType = req.SubType, IsBank = req.IsBank
        };
        _db.Accounts.Add(acc);
        await _db.SaveChangesAsync();
        return Ok(acc);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid id, AccountRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (acc is null) return NotFound();
        acc.Code = req.Code; acc.Name = req.Name; acc.Type = req.Type;
        acc.SubType = req.SubType; acc.IsBank = req.IsBank;
        await _db.SaveChangesAsync();
        return Ok(acc);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (acc is null) return NotFound();
        if (acc.SystemTag != null)
            return BadRequest(new { error = "System accounts cannot be archived." });
        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id))
            acc.Archived = true;                 // keep history intact
        else
            _db.Accounts.Remove(acc);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
