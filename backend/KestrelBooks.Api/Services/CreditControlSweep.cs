namespace KestrelBooks.Api.Services;

/// <summary>
/// Runs the chase ladder once a day. Deliberately daily rather than hourly: a
/// reminder is a business communication, and nobody should receive two in a
/// morning because a container restarted.
/// </summary>
public class CreditControlSweep : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CreditControlSweep> _log;
    private readonly IConfiguration _config;
    public CreditControlSweep(IServiceScopeFactory scopes, ILogger<CreditControlSweep> log, IConfiguration config)
    {
        _scopes = scopes; _log = log; _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off unless explicitly enabled: sending real emails to real customers is
        // not something a deployment should start doing by accident.
        if (!bool.TryParse(_config["CreditControl:AutoSend"], out var enabled) || !enabled)
        {
            _log.LogInformation("Automatic credit control is off (set CreditControl:AutoSend to enable).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CreditControlService>();
                var sent = await svc.RunAllAsync(DateOnly.FromDateTime(DateTime.UtcNow));
                if (sent > 0) _log.LogInformation("Credit control: {Count} reminder(s) sent", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Credit control sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
