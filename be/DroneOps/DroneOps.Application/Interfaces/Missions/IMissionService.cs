using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionService
{
    Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default);
    Task<MissionResponse?> GetByIdAsync(
    Guid missionId,
    CancellationToken cancellationToken = default);
    Task<List<MissionResponse>> GetAllAsync(
    CancellationToken cancellationToken = default);

    Task<List<MissionResponse>> GetMyAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}