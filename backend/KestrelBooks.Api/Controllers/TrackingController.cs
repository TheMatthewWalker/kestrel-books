using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record TrackingCategoryRequest(string Name);
public record TrackingOptionRequest(string Name);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/tracking")]
public class TrackingController : ControllerBase
{
    private const int MaxCategories = 2;

    private readonly AppDbContext _db;
    private readonly AccessService _access;
    public TrackingController(AppDbContext db, AccessService access)
    {
        _db = db; _access = access;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var categories = await _db.TrackingCategories.Include(c => c.Options)
            .Where(c => c.BusinessId == businessId).OrderBy(c => c.Name).ToListAsync();
        return Ok(categories.Select(c => new
        {
            c.Id, c.Name, c.Enabled,
            Options = c.Options.Where(o => !o.Archived).OrderBy(o => o.Name)
                .Select(o => new { o.Id, o.Name }),
        }));
    }

    /// <summary>
    /// Two categories maximum. Not an arbitrary cap: beyond two dimensions the
    /// reports become unreadable and the coding becomes guesswork, which is why
    /// every serious package settled on the same limit.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCategory(Guid businessId, TrackingCategoryRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Accountant);
        var count = await _db.TrackingCategories.CountAsync(c => c.BusinessId == businessId);
        if (count >= MaxCategories)
            return BadRequest(new { error = $"A business can have at most {MaxCategories} tracking categories." });
        var category = new TrackingCategory { Id = Guid.NewGuid(), BusinessId = businessId, Name = req.Name };
        _db.TrackingCategories.Add(category);
        await _db.SaveChangesAsync();
        return Ok(new { category.Id });
    }

    [HttpPost("{categoryId:guid}/options")]
    public async Task<IActionResult> AddOption(Guid businessId, Guid categoryId, TrackingOptionRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var category = await _db.TrackingCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.BusinessId == businessId);
        if (category is null) return NotFound();
        var option = new TrackingOption
        {
            Id = Guid.NewGuid(), BusinessId = businessId,
            TrackingCategoryId = categoryId, Name = req.Name,
        };
        _db.TrackingOptions.Add(option);
        await _db.SaveChangesAsync();
        return Ok(new { option.Id });
    }

    /// <summary>Archive rather than delete — historical journals still reference it.</summary>
    [HttpPost("options/{optionId:guid}/archive")]
    public async Task<IActionResult> ArchiveOption(Guid businessId, Guid optionId, [FromQuery] bool archived = true)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var option = await _db.TrackingOptions
            .FirstOrDefaultAsync(o => o.Id == optionId && o.BusinessId == businessId);
        if (option is null) return NotFound();
        option.Archived = archived;
        await _db.SaveChangesAsync();
        return Ok(new { option.Archived });
    }

    /// <summary>
    /// Profit and loss split by tracking option — the reason the dimension exists.
    /// Aggregated client-side because SQLite (used in tests) cannot translate
    /// decimal sums server-side.
    /// </summary>
    [HttpGet("{categoryId:guid}/profit-and-loss")]
    public async Task<IActionResult> SegmentedPandL(Guid businessId, Guid categoryId,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var options = await _db.TrackingOptions
            .Where(o => o.BusinessId == businessId && o.TrackingCategoryId == categoryId)
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        var rows = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date >= from && l.JournalEntry.Date <= to
                        && (l.Account.Type == AccountType.Income || l.Account.Type == AccountType.Expense))
            .Select(l => new { l.TrackingOptionId, l.Account.Type, l.Debit, l.Credit })
            .ToListAsync();

        var segments = rows
            .GroupBy(r => r.TrackingOptionId)
            .Select(g =>
            {
                var income = g.Where(x => x.Type == AccountType.Income).Sum(x => x.Credit - x.Debit);
                var expense = g.Where(x => x.Type == AccountType.Expense).Sum(x => x.Debit - x.Credit);
                return new
                {
                    OptionId = g.Key,
                    Name = g.Key is Guid id && options.TryGetValue(id, out var n) ? n : "Unallocated",
                    Income = income,
                    Expenses = expense,
                    Profit = income - expense,
                };
            })
            .OrderByDescending(s => s.Profit)
            .ToList();

        return Ok(new
        {
            From = from, To = to,
            Segments = segments,
            TotalProfit = segments.Sum(s => s.Profit),
        });
    }
}
