using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IMissionWorkflowRepository
{
    Task<Mission?> GetByIdWithWaypointsAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Mission mission,
        CancellationToken cancellationToken = default);

    Task UpdatePlanAsync(
        Mission current,
        Mission proposal,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);

    Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(
    Mission mission,
    CancellationToken cancellationToken = default);
}