using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Controllers;

public record CreateBusinessRequest(string Name, string? CompanyNumber, string? VatNumber, int YearStartMonth = 4);

[ApiController]
[Authorize]
[Route("api/businesses")]
public class BusinessesController : ControllerBase
{
    private readonly AppDbContext _db;
    public BusinessesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var uid = AccessService.UserId(User);
        var items = await _db.UserBusinessAccess
            .Where(a => a.UserId == uid)
            .Select(a => new { a.Business.Id, a.Business.Name, a.Business.VatNumber, a.Role })
            .OrderBy(b => b.Name)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateBusinessRequest req)
    {
        var uid = AccessService.UserId(User);
        var business = new Business
        {
            Id = Guid.NewGuid(), Name = req.Name,
            CompanyNumber = req.CompanyNumber, VatNumber = req.VatNumber,
            YearStartMonth = req.YearStartMonth
        };
        _db.Businesses.Add(business);
        _db.UserBusinessAccess.Add(new UserBusinessAccess
        {
            UserId = uid, BusinessId = business.Id, Role = BusinessRole.Owner
        });
        _db.Accounts.AddRange(CoaSeeder.DefaultChart(business.Id));
        await _db.SaveChangesAsync();
        return Ok(new { business.Id, business.Name });
    }
}
