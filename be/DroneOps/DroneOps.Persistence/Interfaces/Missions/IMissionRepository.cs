using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionRepository
{
    Task AddAsync(
        Mission mission,
        CancellationToken cancellationToken = default);

    Task<bool> UavExistsAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);
    Task<Mission?> GetByIdAsync(
    Guid missionId,
    CancellationToken cancellationToken = default);

    Task<(List<Mission> Items, int TotalCount)> GetAllAsync(
        string? search,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<List<Mission>> GetByCreatorAsync(
        Guid creatorId,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);

    Task<Mission?> GetByIdForUpdateAsync(
    Guid missionId,
    CancellationToken cancellationToken = default);
    Task DeleteWaypointsByMissionIdAsync(
     Guid missionId,
     CancellationToken cancellationToken = default);

    Task AddWaypointsAsync(
        IEnumerable<Waypoint> waypoints,
        CancellationToken cancellationToken = default);
    Task<bool> IsUavOwnedByUserAsync(
    Guid uavId,
    Guid userId,
    CancellationToken cancellationToken = default);
}