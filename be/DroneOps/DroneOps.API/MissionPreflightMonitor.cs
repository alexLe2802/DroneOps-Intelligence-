using DroneOps.Application.Interfaces.Missions;

public sealed class MissionPreflightMonitor(
    IServiceScopeFactory scopes,
    ILogger<MissionPreflightMonitor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer =
            new PeriodicTimer(TimeSpan.FromMinutes(1));

        do
        {
            try
            {
                using var scope = scopes.CreateScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IMissionPreflightService>();

                await service.ProcessDueMissionsAsync(
                    stoppingToken);
            }
            catch (Exception ex)
                when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Preflight monitor cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(
            stoppingToken));
    }
}