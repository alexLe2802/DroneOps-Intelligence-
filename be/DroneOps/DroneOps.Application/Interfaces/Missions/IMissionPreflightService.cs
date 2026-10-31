namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionPreflightService
{
    Task ProcessDueMissionsAsync(
        CancellationToken cancellationToken = default);
}