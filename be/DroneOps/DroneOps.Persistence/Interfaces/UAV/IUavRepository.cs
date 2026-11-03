using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

public interface IUavRepository
    : IGenericRepository<UAV>
{
    Task<bool> DeleteOwnedAsync(
        Guid uavId,
        Guid userId,
        bool canManageAll,
        CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<List<UAV>> GetPendingAsync(
     string? keyword = null,
     int page = 1,
     int pageSize = 10,
     CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<UAV> Items, int TotalCount)> GetByPilotIdAsync(
     Guid pilotId,
     string? keyword,
     string? status,
     int pageNumber,
     int pageSize,
     CancellationToken cancellationToken = default);

    Task<UAV?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<UAV> Items,
      int TotalCount)> GetAllPagedAsync(
          string? keyword,
          string? status,
          int pageNumber,
          int pageSize,
          CancellationToken cancellationToken = default);
}
