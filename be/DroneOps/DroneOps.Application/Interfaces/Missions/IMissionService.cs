using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionService
{
    Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default);
}