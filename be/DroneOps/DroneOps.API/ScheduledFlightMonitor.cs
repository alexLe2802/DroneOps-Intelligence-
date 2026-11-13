using DroneOps.API;
public sealed class ScheduledFlightMonitor(IServiceScopeFactory scopes, ILogger<ScheduledFlightMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<FlightControlService>().DispatchDueAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Scheduled dispatch cycle failed."); }
            await Task.Delay(250, ct);
        }
    }
}
