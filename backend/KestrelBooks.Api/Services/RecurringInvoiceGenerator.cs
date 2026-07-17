namespace KestrelBooks.Api.Services;

/// <summary>
/// Daily background sweep that generates due recurring invoices across all
/// businesses. Mirrors AuthMaintenanceService: its own DI scope per run, a
/// PeriodicTimer, and swallow-and-log so one bad template never kills the loop.
/// A fresh scope per run keeps the scoped TenantProvider clean between passes.
/// </summary>
public class RecurringInvoiceGenerator : BackgroundService
{
    private static readonly Guid SystemUser = Guid.Empty;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RecurringInvoiceGenerator> _log;
    public RecurringInvoiceGenerator(IServiceScopeFactory scopes, ILogger<RecurringInvoiceGenerator> log)
    {
        _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so the app finishes booting/migrating first.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<RecurringInvoiceService>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var count = await svc.RunAllDueAsync(today, SystemUser);
                if (count > 0)
                    _log.LogInformation("Recurring invoices: generated {Count} invoice(s)", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Recurring invoice generation run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
