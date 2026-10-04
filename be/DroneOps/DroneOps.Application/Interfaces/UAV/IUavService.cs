using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.UAVs;

namespace DroneOps.Application.Interfaces.UAVs;

public interface IUavService
{
    Task<UavResponse> CreateAsync(
        Guid userId,
        CreateUavRequest request,
        CancellationToken cancellationToken = default);

    Task<List<UavResponse>> GetMyUavsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<List<UavResponse>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<UavResponse?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<UavResponse?> ApproveAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<UavResponse?> RejectAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<UavResponse?> ActivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<UavResponse?> DeactivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);
}