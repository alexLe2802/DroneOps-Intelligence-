using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

public interface IUavRepository
    : IGenericRepository<UAV>
{
    Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<List<UAV>> GetPendingAsync(
     string? keyword = null,
     int page = 1,
     int pageSize = 10,
     CancellationToken cancellationToken = default);

    Task<List<UAV>> GetByPilotIdAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default);

    Task<UAV?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);
}