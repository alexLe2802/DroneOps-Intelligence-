using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionPreflightService(
    IMissionPreflightRepository repository,
    IMissionValidationService validation,
    ILogger<MissionPreflightService> logger)
    : IMissionPreflightService
{
    public async Task ProcessDueMissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var dueMissions =
            await repository.GetDueMissionsAsync(
                DateTime.UtcNow,
                100,
                cancellationToken);

        await Parallel.ForEachAsync(
            dueMissions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            },
            async (mission, ct) =>
            {
                try
                {
                    await validation.RevalidateAsync(
                        mission.Id,
                        mission.CreatedBy,
                        true,
                        "Preflight",
                        ct);
                }
                catch (Exception ex)
                    when (!ct.IsCancellationRequested)
                {
                    logger.LogError(
                        ex,
                        "Preflight check failed for {MissionId}",
                        mission.Id);
                }
            });
    }
}