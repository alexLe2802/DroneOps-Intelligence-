using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IPilotRepository
    : IGenericRepository<Pilot>
{
    Task<Pilot?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}