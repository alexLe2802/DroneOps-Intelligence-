using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Validators;

public interface IMissionValidationService
{
    Task<MissionValidationResult?> LatestAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult?> LatestPreflightAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult?> ReadAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult?> ReadLatestRunAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult?> ReadRunAsync(
        Guid missionId,
        Guid runId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult> EvaluateAsync(
        Mission mission,
        string stage,
        CancellationToken cancellationToken = default);

    Task StoreAsync(
        Mission mission,
        Guid actor,
        MissionValidationResult result,
        bool persisted = true,
        CancellationToken cancellationToken = default);

    Task<MissionValidationResult> RevalidateAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        string stage,
        CancellationToken cancellationToken = default);

    Task SyncPreflightAlertAsync(
        Guid missionId,
        MissionValidationResult result,
        CancellationToken cancellationToken = default);
}