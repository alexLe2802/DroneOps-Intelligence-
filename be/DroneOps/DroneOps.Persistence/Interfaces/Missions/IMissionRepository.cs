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

    Task<List<Mission>> GetAllAsync(
     CancellationToken cancellationToken = default);

    Task<List<Mission>> GetByCreatorAsync(
        Guid creatorId,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}