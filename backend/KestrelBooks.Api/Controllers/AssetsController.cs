using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record AssetRequest(string Code, string Description, string? Category, AssetStatus Status,
    DateOnly AcquisitionDate, decimal Cost, decimal ResidualValue, DepreciationMethod Method,
    int UsefulLifeMonths, decimal AnnualRatePercent, DateOnly DepreciationStart,
    Guid CostAccountId, Guid AccumDepAccountId, Guid DepExpenseAccountId, string? Notes);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/assets")]
public class AssetsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly DepreciationService _depreciation;
    public AssetsController(AppDbContext db, AccessService access, DepreciationService depreciation)
    {
        _db = db; _access = access; _depreciation = depreciation;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var assets = await _db.FixedAssets.Where(a => a.BusinessId == businessId)
            .OrderBy(a => a.Code).ToListAsync();
        return Ok(assets.Select(a => new
        {
            a.Id, a.Code, a.Description, a.Category, a.Status, a.AcquisitionDate,
            a.Cost, a.ResidualValue, a.Method, a.UsefulLifeMonths, a.AnnualRatePercent,
            a.DepreciationStart, a.DepreciatedThrough, a.AccumulatedDepreciation,
            NetBookValue = a.NetBookValue,
            NextMonthlyCharge = a.Status == AssetStatus.InUse ? DepreciationService.MonthlyCharge(a) : 0
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, AssetRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var asset = new FixedAsset { Id = Guid.NewGuid(), BusinessId = businessId };
        Apply(asset, req);
        _db.FixedAssets.Add(asset);
        await _db.SaveChangesAsync();
        return Ok(new { asset.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid id, AssetRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var asset = await _db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == businessId);
        if (asset is null) return NotFound();
        if (asset.DepreciatedThrough != null && (req.Cost != asset.Cost || req.Method != asset.Method))
            return BadRequest(new { error = "Cost and method are locked once depreciation has been posted. Dispose and re-add, or adjust by journal." });
        Apply(asset, req);
        await _db.SaveChangesAsync();
        return Ok(new { asset.Id });
    }

    /// <summary>Run monthly depreciation for the whole business — one balanced journal, auto-posted.</summary>
    [HttpPost("depreciation-run")]
    public async Task<IActionResult> RunDepreciation(Guid businessId, [FromQuery] int year, [FromQuery] int month)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var journal = await _depreciation.RunMonthAsync(businessId, year, month, AccessService.UserId(User));
        return journal is null
            ? Ok(new { message = "Nothing to depreciate for that month." })
            : Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    /// <summary>Capitalise an asset under construction into use.</summary>
    [HttpPost("{id:guid}/capitalise")]
    public async Task<IActionResult> Capitalise(Guid businessId, Guid id, [FromQuery] DateOnly date)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var journal = await _depreciation.CapitaliseAsync(businessId, id, date, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    private static void Apply(FixedAsset a, AssetRequest req)
    {
        a.Code = req.Code; a.Description = req.Description; a.Category = req.Category;
        a.Status = req.Status; a.AcquisitionDate = req.AcquisitionDate;
        a.Cost = req.Cost; a.ResidualValue = req.ResidualValue; a.Method = req.Method;
        a.UsefulLifeMonths = req.UsefulLifeMonths; a.AnnualRatePercent = req.AnnualRatePercent;
        a.DepreciationStart = req.DepreciationStart;
        a.CostAccountId = req.CostAccountId; a.AccumDepAccountId = req.AccumDepAccountId;
        a.DepExpenseAccountId = req.DepExpenseAccountId; a.Notes = req.Notes;
    }
}
