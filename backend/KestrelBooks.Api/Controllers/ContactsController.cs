using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record ContactRequest(string Name, string? Email, string? Phone,
    string? AddressLine1, string? AddressLine2, string? City, string? Postcode,
    string? VatNumber, int PaymentTermsDays, string? Notes);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class ContactsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccessService _access;
    public ContactsController(AppDbContext db, AccessService access) { _db = db; _access = access; }

    [HttpGet("customers")]
    public async Task<IActionResult> Customers(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Customers.Where(c => c.BusinessId == businessId && !c.Archived)
            .OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost("customers")]
    public async Task<IActionResult> CreateCustomer(Guid businessId, ContactRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var c = new Customer { Id = Guid.NewGuid(), BusinessId = businessId };
        Apply(c, req);
        _db.Customers.Add(c);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpPut("customers/{id:guid}")]
    public async Task<IActionResult> UpdateCustomer(Guid businessId, Guid id, ContactRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (c is null) return NotFound();
        Apply(c, req);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpGet("vendors")]
    public async Task<IActionResult> Vendors(Guid businessId)
    {
        await _access.EnsureAccessAsync(User, businessId);
        return Ok(await _db.Vendors.Where(v => v.BusinessId == businessId && !v.Archived)
            .OrderBy(v => v.Name).ToListAsync());
    }

    [HttpPost("vendors")]
    public async Task<IActionResult> CreateVendor(Guid businessId, ContactRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var v = new Vendor { Id = Guid.NewGuid(), BusinessId = businessId };
        Apply(v, req);
        _db.Vendors.Add(v);
        await _db.SaveChangesAsync();
        return Ok(v);
    }

    [HttpPut("vendors/{id:guid}")]
    public async Task<IActionResult> UpdateVendor(Guid businessId, Guid id, ContactRequest req)
    {
        await _access.EnsureAccessAsync(User, businessId);
        var v = await _db.Vendors.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId);
        if (v is null) return NotFound();
        Apply(v, req);
        await _db.SaveChangesAsync();
        return Ok(v);
    }

    private static void Apply(Contact c, ContactRequest req)
    {
        c.Name = req.Name; c.Email = req.Email; c.Phone = req.Phone;
        c.AddressLine1 = req.AddressLine1; c.AddressLine2 = req.AddressLine2;
        c.City = req.City; c.Postcode = req.Postcode; c.VatNumber = req.VatNumber;
        c.PaymentTermsDays = req.PaymentTermsDays; c.Notes = req.Notes;
    }
}
