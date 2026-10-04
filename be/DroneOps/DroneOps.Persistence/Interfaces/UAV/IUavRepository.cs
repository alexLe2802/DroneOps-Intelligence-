using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

public interface IUavRepository
    : IGenericRepository<UAV>
{
    Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<List<UAV>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<List<UAV>> GetByPilotIdAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default);

    Task<UAV?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);
}