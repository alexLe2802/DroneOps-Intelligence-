using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionApprovalService
{
    Task<PagedPendingApprovalsResponse> GetPendingApprovalsAsync(
        GetPendingApprovalsRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default);

    Task<MissionDecisionResponse> ApproveMissionAsync(
        Guid missionId,
        ApproveMissionRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default);

    Task<MissionDecisionResponse> RejectMissionAsync(
        Guid missionId,
        RejectMissionRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default);
}