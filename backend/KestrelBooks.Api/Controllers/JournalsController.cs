using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record JournalLineRequest(Guid AccountId, decimal Debit, decimal Credit, string? Description);
public record JournalRequest(DateOnly Date, string Reference, string Narrative, List<JournalLineRequest> Lines);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/journals")]
public class JournalsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly PostingService _posting;
    public JournalsController(AppDbContext db, AccessService access, PostingService posting)
    {
        _db = db; _access = access; _posting = posting;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Journals.Where(j => j.BusinessId == businessId)
            .OrderByDescending(j => j.Date).ThenByDescending(j => j.Number)
            .Take(300)
            .Select(j => new { j.Id, j.Number, j.Date, j.Reference, j.Narrative, j.Status,
                               j.Source, j.ReversalOfId,
                               Total = j.Lines.Sum(l => l.Debit) })
            .ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var j = await _db.Journals
            .Where(x => x.Id == id && x.BusinessId == businessId)
            .Select(x => new
            {
                x.Id, x.Number, x.Date, x.Reference, x.Narrative, x.Status, x.Source,
                x.ReversalOfId, x.PostedAtUtc,
                Lines = x.Lines.Select(l => new
                {
                    l.Id, l.AccountId, AccountCode = l.Account.Code, AccountName = l.Account.Name,
                    l.Debit, l.Credit, l.Description
                })
            })
            .FirstOrDefaultAsync();
        return j is null ? NotFound() : Ok(j);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDraft(Guid businessId, JournalRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var entry = await _posting.CreateDraftAsync(businessId, AccessService.UserId(User),
            req.Date, req.Reference, req.Narrative, SourceType.Manual, null,
            req.Lines.Select(l => new DraftLine(l.AccountId, l.Debit, l.Credit, l.Description)));
        return Ok(new { entry.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDraft(Guid businessId, Guid id, JournalRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var entry = await _db.Journals.Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == id && j.BusinessId == businessId);
        if (entry is null) return NotFound();
        if (entry.Status != JournalStatus.Draft)
            return BadRequest(new { error = "Posted journals are immutable — post a reversal instead." });
        entry.Date = req.Date; entry.Reference = req.Reference; entry.Narrative = req.Narrative;
        entry.Lines.Clear();
        entry.Lines.AddRange(req.Lines.Select(l => new JournalLine
        {
            Id = Guid.NewGuid(), AccountId = l.AccountId,
            Debit = Math.Round(l.Debit, 2), Credit = Math.Round(l.Credit, 2),
            Description = l.Description
        }));
        _posting.Validate(entry);
        await _db.SaveChangesAsync();
        return Ok(new { entry.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var entry = await _db.Journals.FirstOrDefaultAsync(j => j.Id == id && j.BusinessId == businessId);
        if (entry is null) return NotFound();
        if (entry.Status != JournalStatus.Draft)
            return BadRequest(new { error = "Posted journals cannot be deleted — reverse instead." });
        _db.Journals.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var entry = await _posting.PostAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { entry.Id, entry.Number });
    }

    [HttpPost("{id:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid businessId, Guid id, [FromQuery] DateOnly? date)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var reversal = await _posting.ReverseAsync(businessId, id, AccessService.UserId(User), date);
        return Ok(new { reversal.Id, reversal.Number });
    }
}
