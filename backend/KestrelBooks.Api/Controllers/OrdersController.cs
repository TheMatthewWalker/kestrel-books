using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record OrderLineDto(Guid? ItemId, string Description, decimal Quantity, decimal UnitPrice,
    VatRate VatRate, Guid AccountId);
public record QuoteRequest(Guid CustomerId, string Number, DateOnly Date, DateOnly ExpiryDate,
    string? Reference, string? Notes, List<OrderLineDto> Lines);
public record PurchaseOrderRequest(Guid VendorId, string Number, DateOnly Date, DateOnly? ExpectedDate,
    string? Reference, string? Notes, List<OrderLineDto> Lines);
public record ConvertRequest(string InvoiceNumber, DateOnly? InvoiceDate);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    private readonly OrderService _orders;
    public OrdersController(AppDbContext db, AccessService access, OrderService orders)
    {
        _db = db; _access = access; _orders = orders;
    }

    // ---------- Quotes ----------

    [HttpGet("quotes")]
    public async Task<IActionResult> Quotes(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var quotes = await _db.SalesQuotes.Include(q => q.Lines)
            .Where(q => q.BusinessId == businessId)
            .OrderByDescending(q => q.Date).Take(300).ToListAsync();
        return Ok(quotes.Select(q => new
        {
            q.Id, q.Number, Contact = q.Customer.Name, q.CustomerId, q.Date, q.ExpiryDate,
            q.Status, q.NetTotal, q.VatTotal, q.GrossTotal, q.ConvertedInvoiceId,
            LineCount = q.Lines.Count,
        }));
    }

    [HttpPost("quotes")]
    public async Task<IActionResult> CreateQuote(Guid businessId, QuoteRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var quote = new SalesQuote
        {
            Id = Guid.NewGuid(), BusinessId = businessId, CustomerId = req.CustomerId,
            Number = req.Number, Date = req.Date, ExpiryDate = req.ExpiryDate,
            Reference = req.Reference, Notes = req.Notes,
        };
        foreach (var l in req.Lines)
            quote.Lines.Add(new SalesQuoteLine
            {
                Id = Guid.NewGuid(), SalesQuoteId = quote.Id, ItemId = l.ItemId,
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                VatRate = l.VatRate, AccountId = l.AccountId,
            });
        _db.SalesQuotes.Add(quote);
        await _db.SaveChangesAsync();
        return Ok(new { quote.Id });
    }

    [HttpPost("quotes/{id:guid}/status")]
    public async Task<IActionResult> SetQuoteStatus(Guid businessId, Guid id, [FromQuery] QuoteStatus status)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var quote = await _db.SalesQuotes.FirstOrDefaultAsync(q => q.Id == id && q.BusinessId == businessId);
        if (quote is null) return NotFound();
        if (quote.Status == QuoteStatus.Converted)
            return BadRequest(new { error = "An invoiced quote cannot change status." });
        quote.Status = status;
        await _db.SaveChangesAsync();
        return Ok(new { quote.Status });
    }

    [HttpPost("quotes/{id:guid}/convert")]
    public async Task<IActionResult> ConvertQuote(Guid businessId, Guid id, ConvertRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var invoice = await _orders.ConvertQuoteAsync(businessId, id, req.InvoiceNumber,
            req.InvoiceDate, AccessService.UserId(User));
        return Ok(new { invoiceId = invoice.Id, invoice.Number, invoice.GrossTotal });
    }

    [HttpDelete("quotes/{id:guid}")]
    public async Task<IActionResult> DeleteQuote(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var quote = await _db.SalesQuotes.FirstOrDefaultAsync(q => q.Id == id && q.BusinessId == businessId);
        if (quote is null) return NotFound();
        if (quote.Status == QuoteStatus.Converted)
            return BadRequest(new { error = "An invoiced quote cannot be deleted." });
        _db.SalesQuotes.Remove(quote);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("quotes/expire-stale")]
    public async Task<IActionResult> ExpireStale(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var count = await _orders.ExpireStaleQuotesAsync(businessId, DateOnly.FromDateTime(DateTime.UtcNow));
        return Ok(new { expired = count });
    }

    // ---------- Purchase orders ----------

    [HttpGet("purchase-orders")]
    public async Task<IActionResult> Orders(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var orders = await _db.PurchaseOrders.Include(o => o.Lines)
            .Where(o => o.BusinessId == businessId)
            .OrderByDescending(o => o.Date).Take(300).ToListAsync();
        return Ok(orders.Select(o => new
        {
            o.Id, o.Number, Contact = o.Vendor.Name, o.VendorId, o.Date, o.ExpectedDate,
            o.Status, o.NetTotal, o.VatTotal, o.GrossTotal, o.ConvertedInvoiceId,
            LineCount = o.Lines.Count,
        }));
    }

    [HttpPost("purchase-orders")]
    public async Task<IActionResult> CreateOrder(Guid businessId, PurchaseOrderRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var order = new PurchaseOrder
        {
            Id = Guid.NewGuid(), BusinessId = businessId, VendorId = req.VendorId,
            Number = req.Number, Date = req.Date, ExpectedDate = req.ExpectedDate,
            Reference = req.Reference, Notes = req.Notes,
        };
        foreach (var l in req.Lines)
            order.Lines.Add(new PurchaseOrderLine
            {
                Id = Guid.NewGuid(), PurchaseOrderId = order.Id, ItemId = l.ItemId,
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                VatRate = l.VatRate, AccountId = l.AccountId,
            });
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();
        return Ok(new { order.Id });
    }

    [HttpPost("purchase-orders/{id:guid}/status")]
    public async Task<IActionResult> SetOrderStatus(Guid businessId, Guid id,
        [FromQuery] PurchaseOrderStatus status)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId);
        if (order is null) return NotFound();
        if (order.Status == PurchaseOrderStatus.Converted)
            return BadRequest(new { error = "An invoiced order cannot change status." });
        order.Status = status;
        await _db.SaveChangesAsync();
        return Ok(new { order.Status });
    }

    [HttpPost("purchase-orders/{id:guid}/convert")]
    public async Task<IActionResult> ConvertOrder(Guid businessId, Guid id, ConvertRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var invoice = await _orders.ConvertOrderAsync(businessId, id, req.InvoiceNumber,
            req.InvoiceDate, AccessService.UserId(User));
        return Ok(new { invoiceId = invoice.Id, invoice.Number, invoice.GrossTotal });
    }

    [HttpDelete("purchase-orders/{id:guid}")]
    public async Task<IActionResult> DeleteOrder(Guid businessId, Guid id)
    {
        await _access.EnsureAccessAsync(User, businessId, BusinessRole.Bookkeeper);
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId);
        if (order is null) return NotFound();
        if (order.Status == PurchaseOrderStatus.Converted)
            return BadRequest(new { error = "An invoiced order cannot be deleted." });
        _db.PurchaseOrders.Remove(order);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
