using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record MoneyRequest(MoneyDirection Direction, DateOnly Date, string Reference, decimal Amount,
    Guid BankAccountId, Guid? CustomerId, Guid? VendorId,
    Guid? SalesInvoiceId, Guid? PurchaseInvoiceId, Guid? DirectAccountId, string? Notes);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/money")]
public class MoneyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly DocumentPostingService _docs;
    public MoneyController(AppDbContext db, AccessService access, DocumentPostingService docs)
    {
        _db = db; _access = access; _docs = docs;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var (skip, take) = Paging.Normalise(ref page, ref pageSize);
        var query = _db.MoneyTransactions.Where(t => t.BusinessId == businessId);
        Response.Headers["X-Total-Count"] = (await query.CountAsync()).ToString();
        return Ok(await query.OrderByDescending(t => t.Date).Skip(skip).Take(take).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid businessId, MoneyRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var tx = new MoneyTransaction { Id = Guid.NewGuid(), BusinessId = businessId };
        Apply(tx, req);
        _db.MoneyTransactions.Add(tx);
        await _db.SaveChangesAsync();
        return Ok(new { tx.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid id, MoneyRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var tx = await _db.MoneyTransactions.FirstOrDefaultAsync(t => t.Id == id && t.BusinessId == businessId);
        if (tx is null) return NotFound();
        if (tx.Status != DocumentStatus.Draft)
            return BadRequest(new { error = "Posted transactions are locked — reverse the journal to correct." });
        Apply(tx, req);
        await _db.SaveChangesAsync();
        return Ok(new { tx.Id });
    }

    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var journal = await _docs.PostMoneyTransactionAsync(businessId, id, AccessService.UserId(User));
        return Ok(new { journalId = journal.Id, journalNumber = journal.Number });
    }

    private static void Apply(MoneyTransaction tx, MoneyRequest req)
    {
        tx.Direction = req.Direction; tx.Date = req.Date; tx.Reference = req.Reference;
        tx.Amount = req.Amount; tx.BankAccountId = req.BankAccountId;
        tx.CustomerId = req.CustomerId; tx.VendorId = req.VendorId;
        tx.SalesInvoiceId = req.SalesInvoiceId; tx.PurchaseInvoiceId = req.PurchaseInvoiceId;
        tx.DirectAccountId = req.DirectAccountId; tx.Notes = req.Notes;
    }
}
