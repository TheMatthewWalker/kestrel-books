using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KestrelBooks.Api.Controllers;

/// <summary>
/// Practice-wide views spanning every client the signed-in user can access.
/// Not business-scoped: no {businessId} in the route, so TenantMiddleware
/// doesn't engage and the service queries with filters explicitly ignored,
/// bounded to the user's own access list.
/// </summary>
[ApiController]
[Authorize]
[Route("api/practice")]
public class PracticeController : ControllerBase
{
    private readonly PracticeDashboardService _dashboard;
    public PracticeController(PracticeDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int horizonDays = 60)
    {
        var userId = AccessService.UserId(User);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await _dashboard.BuildAsync(userId, today, horizonDays));
    }
}
