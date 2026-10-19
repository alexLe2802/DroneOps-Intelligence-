using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IPilotRepository
    : IGenericRepository<Pilot>
{
    Task<Pilot?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<Pilot?> GetByUserIdForUpdateAsync(
     Guid userId,
     CancellationToken cancellationToken = default);

    Task<bool> HasPendingUpdateRequestAsync(
    Guid pilotId,
    CancellationToken cancellationToken = default);

    Task AddUpdateRequestAsync(
        PilotUpdateRequest request,
        CancellationToken cancellationToken = default);
}