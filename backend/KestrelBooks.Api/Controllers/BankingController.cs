using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record CreateFromLineRequest(Guid? DirectAccountId, Guid? SalesInvoiceId, Guid? PurchaseInvoiceId);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/banking")]
public class BankingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly BankImportService _bank;
    private readonly DocumentPostingService _docs;
    public BankingController(AppDbContext db, AccessService access, BankImportService bank, DocumentPostingService docs)
    {
        _db = db; _access = access; _bank = bank; _docs = docs;
    }

    /// <summary>Import a CSV or OFX statement export for a bank account.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(Guid businessId, [FromQuery] Guid bankAccountId, IFormFile file)
    {
        await _access.EnsureAccessAsync(User, businessId);
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file received." });
        using var reader = new StreamReader(file.OpenReadStream());
        var import = await _bank.ImportAsync(businessId, bankAccountId, file.FileName, await reader.ReadToEndAsync());
        return Ok(new { import.Id, imported = import.LineCount });
    }

    /// <summary>Statement lines for an account with match suggestions and reconciliation progress.</summary>
    [HttpGet("lines")]
    public async Task<IActionResult> Lines(Guid businessId, [FromQuery] Guid bankAccountId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var lines = await _db.BankStatementLines
            .Where(l => l.BusinessId == businessId && l.BankAccountId == bankAccountId)
            .OrderByDescending(l => l.Date)
            .Take(400)
            .Select(l => new { l.Id, l.Date, l.Description, l.Amount, l.Status, l.MatchedJournalLineId })
            .ToListAsync();
        var suggestions = await _bank.SuggestAsync(businessId, bankAccountId);
        var total = lines.Count;
        var done = lines.Count(l => l.Status != StatementLineStatus.Unmatched);
        return Ok(new
        {
            progress = new { total, reconciled = done },
            lines = lines.Select(l => new
            {
                l.Id, l.Date, l.Description, l.Amount, l.Status,
                suggestions = suggestions.TryGetValue(l.Id, out var s) ? s : new List<MatchSuggestion>()
            })
        });
    }

    [HttpPost("lines/{lineId:guid}/match/{journalLineId:guid}")]
    public async Task<IActionResult> Match(Guid businessId, Guid lineId, Guid journalLineId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        await _bank.MatchAsync(businessId, lineId, journalLineId);
        return Ok(new { matched = true });
    }

    [HttpPost("lines/{lineId:guid}/exclude")]
    public async Task<IActionResult> Exclude(Guid businessId, Guid lineId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var line = await _db.BankStatementLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.BusinessId == businessId);
        if (line is null) return NotFound();
        line.Status = StatementLineStatus.Excluded;
        await _db.SaveChangesAsync();
        return Ok(new { excluded = true });
    }

    /// <summary>
    /// Create and post a receipt/payment straight from an unmatched statement line
    /// (direction and amount come from the line), then mark the line reconciled.
    /// </summary>
    [HttpPost("lines/{lineId:guid}/create-transaction")]
    public async Task<IActionResult> CreateTransaction(Guid businessId, Guid lineId, CreateFromLineRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var line = await _db.BankStatementLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.BusinessId == businessId);
        if (line is null) return NotFound();
        if (line.Status != StatementLineStatus.Unmatched)
            return BadRequest(new { error = "Line is already reconciled." });

        var tx = new MoneyTransaction
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Direction = line.Amount > 0 ? MoneyDirection.In : MoneyDirection.Out,
            Date = line.Date,
            Reference = line.Description.Length > 60 ? line.Description[..60] : line.Description,
            Amount = Math.Abs(line.Amount),
            BankAccountId = line.BankAccountId,
            SalesInvoiceId = req.SalesInvoiceId,
            PurchaseInvoiceId = req.PurchaseInvoiceId,
            DirectAccountId = req.DirectAccountId,
            Notes = "Created from bank statement line",
        };
        _db.MoneyTransactions.Add(tx);
        await _db.SaveChangesAsync();
        var journal = await _docs.PostMoneyTransactionAsync(businessId, tx.Id, AccessService.UserId(User));

        line.Status = StatementLineStatus.Matched;
        line.CreatedMoneyTransactionId = tx.Id;
        line.MatchedJournalLineId = journal.Lines.First(l => l.AccountId == line.BankAccountId).Id;
        await _db.SaveChangesAsync();
        return Ok(new { journalNumber = journal.Number });
    }
}
