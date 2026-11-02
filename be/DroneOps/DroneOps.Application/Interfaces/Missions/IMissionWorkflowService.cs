using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces;

public interface IMissionWorkflowService
{
    Task<Mission> SavePlanAsync(
        Guid actor,
        CreateMissionRequest request,
        Guid? id,
        CancellationToken cancellationToken = default);
    Task ConfirmHighRiskAsync(
       Guid missionId,
       Guid pilotId,
       CancellationToken cancellationToken = default);

    Task CancelHighRiskAsync(
        Guid missionId,
        Guid pilotId,
        CancellationToken cancellationToken = default);
    Task CancelAsync(
        Guid id,
        Guid actor,
        bool manager,
        string reason,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult> StartAsync(
        Guid id,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default);
}