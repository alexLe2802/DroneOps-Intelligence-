namespace DroneOps.Persistence.Interfaces;

public interface IMissionPreflightRepository
{
    Task<List<(Guid Id, Guid CreatedBy)>> GetDueMissionsAsync(
        DateTime now,
        int limit,
        CancellationToken cancellationToken = default);
}