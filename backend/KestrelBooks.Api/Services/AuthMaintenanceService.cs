using KestrelBooks.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Daily housekeeping: dead refresh tokens and stale one-time codes don't
/// accumulate forever. Auth audit events are retained (2 years) — they're
/// the security record, not clutter.
/// </summary>
public class AuthMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuthMaintenanceService> _log;
    public AuthMaintenanceService(IServiceScopeFactory scopes, ILogger<AuthMaintenanceService> log)
    {
        _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;

                var tokens = await db.RefreshTokens
                    .Where(t => t.ExpiresAtUtc < now.AddDays(-7)
                                || (t.RevokedAtUtc != null && t.RevokedAtUtc < now.AddDays(-30)))
                    .ExecuteDeleteAsync(stoppingToken);
                var codes = await db.OneTimeCodes
                    .Where(c => c.ExpiresAtUtc < now.AddDays(-1))
                    .ExecuteDeleteAsync(stoppingToken);
                var events = await db.AuthEvents
                    .Where(e => e.AtUtc < now.AddYears(-2))
                    .ExecuteDeleteAsync(stoppingToken);

                if (tokens + codes + events > 0)
                    _log.LogInformation(
                        "Auth maintenance: removed {Tokens} refresh tokens, {Codes} one-time codes, {Events} expired audit events",
                        tokens, codes, events);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Auth maintenance run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
