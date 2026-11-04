using DroneOps.Application.Interfaces.Missions;
using DroneOps.API;

public sealed class MissionPreflightMonitor(
    IServiceScopeFactory scopes,
    ILogger<MissionPreflightMonitor> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        RunAsync("Preflight", TimeSpan.FromSeconds(10),
            (services, ct) => services.GetRequiredService<IMissionPreflightService>().ProcessDueMissionsAsync(ct), stoppingToken),
        RunAsync("Preparation reminders", TimeSpan.FromSeconds(10),
            (services, ct) => services.GetRequiredService<FlightPreparationReminders>().ProcessDueAsync(ct), stoppingToken));

    private async Task RunAsync(string name, TimeSpan interval,
        Func<IServiceProvider, CancellationToken, Task> process, CancellationToken stoppingToken)
    {
        // Separate scopes/loops keep slow weather validation from delaying reminders.
        // The existing preflight repository only selects stale validation runs.
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await process(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError(error, "{Monitor} cycle failed", name); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
