using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWorkflowService(
    IMissionWorkflowRepository missionRepository,
    IMissionValidationService validation)
    : IMissionWorkflowService
{
    // =========================================================
    // CREATE / UPDATE MISSION
    // =========================================================

    public async Task<Mission> SavePlanAsync(
        Guid actor,
        CreateMissionRequest request,
        Guid? id,
        CancellationToken cancellationToken = default)
    {
        return await missionRepository.ExecuteSerializableAsync(
            async ct =>
            {
                var isUpdate = id.HasValue;

                Mission current;

                // =========================
                // GET CURRENT MISSION
                // =========================

                if (isUpdate)
                {
                    current = await missionRepository
                        .GetByIdWithWaypointsAsync(
                            id!.Value,
                            ct)
                        ?? throw new KeyNotFoundException(
                            "Mission not found.");

                    if (current.CreatedBy != actor)
                    {
                        throw new UnauthorizedAccessException(
                            "You may only edit your own mission.");
                    }

                    if (current.Status is not (
                        "Draft" or
                        "Rejected" or
                        "Pending" or
                        "Pending Review" or
                        "Submitted" or
                        "Approved"))
                    {
                        throw new InvalidOperationException(
                            "This mission cannot be edited in its current state.");
                    }
                }
                else
                {
                    current = new Mission
                    {
                        Id = Guid.NewGuid(),
                        CreatedBy = actor,
                        CreatedAt = DateTime.UtcNow,
                        Status = "Pending Review"
                    };
                }

                // =========================
                // BUILD PROPOSAL
                // =========================

                var proposal = MissionPlans.Propose(
                    current,
                    request);

                proposal.Status = "Pending Review";

                // =========================
                // VALIDATE
                // =========================

                var validationResult =
                    await validation.EvaluateAsync(
                        proposal,
                        isUpdate
                            ? "Update"
                            : "Creation",
                        ct);

                if (!validationResult.IsValid)
                {
                    throw new MissionValidationException(
                        validationResult);
                }

                // =========================
                // CREATE
                // =========================

                if (!isUpdate)
                {
                    proposal.Id = current.Id;
                    proposal.CreatedBy = current.CreatedBy;
                    proposal.CreatedAt = current.CreatedAt;
                    proposal.Status = "Pending Review";

                    foreach (var waypoint in proposal.Waypoints)
                    {
                        waypoint.MissionId = proposal.Id;
                    }

                    await missionRepository.AddAsync(
                        proposal,
                        ct);

                    await validation.StoreAsync(
                        proposal,
                        actor,
                        validationResult,
                        true,
                        ct);

                    return proposal;
                }

                // =========================
                // UPDATE
                // =========================

                await missionRepository.UpdatePlanAsync(
                    current,
                    proposal,
                    ct);

                current.Status = "Pending Review";

                await validation.StoreAsync(
                    current,
                    actor,
                    validationResult,
                    true,
                    ct);

                return current;
            },
            cancellationToken);
    }

    // =========================================================
    // CANCEL MISSION
    // =========================================================

    public async Task CancelAsync(
        Guid id,
        Guid actor,
        bool manager,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Cancellation reason is required.");
        }

        await missionRepository.ExecuteSerializableAsync(
            async ct =>
            {
                var mission = await missionRepository
                    .GetByIdWithWaypointsAsync(
                        id,
                        ct)
                    ?? throw new KeyNotFoundException(
                        "Mission not found.");

                if (!manager &&
                    mission.CreatedBy != actor)
                {
                    throw new UnauthorizedAccessException(
                        "You may only cancel your own mission.");
                }

                if (mission.Status is
                    "InProgress" or
                    "In Progress" or
                    "Completed")
                {
                    throw new InvalidOperationException(
                        "Only pre-flight missions can be cancelled.");
                }

                if (mission.Status == "Cancelled")
                {
                    throw new InvalidOperationException(
                        "Mission is already cancelled.");
                }

                mission.Status = "Cancelled";

                return true;
            },
            cancellationToken);
    }

    // =========================================================
    // START MISSION
    // =========================================================

    public async Task<MissionValidationResult> StartAsync(
        Guid id,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default)
    {
        return await missionRepository.ExecuteSerializableAsync(
            async ct =>
            {
                var mission = await missionRepository
                    .GetByIdWithWaypointsAsync(
                        id,
                        ct)
                    ?? throw new KeyNotFoundException(
                        "Mission not found.");

                // =========================
                // PERMISSION
                // =========================

                if (!manager &&
                    mission.CreatedBy != actor)
                {
                    throw new UnauthorizedAccessException(
                        "You may only start your own mission.");
                }

                // =========================
                // STATUS
                // =========================

                if (mission.Status != "Approved")
                {
                    throw new InvalidOperationException(
                        "Only an approved mission can start.");
                }

                // =========================
                // FLIGHT WINDOW
                // =========================

                if (mission.StartTime is null ||
                    mission.EndTime is null)
                {
                    throw new InvalidOperationException(
                        "Mission flight window is missing.");
                }

                var now = DateTime.UtcNow;

                if (mission.StartTime > now ||
                    mission.EndTime <= now)
                {
                    throw new InvalidOperationException(
                        "Mission start must be within its scheduled flight window.");
                }

                // =========================
                // PREFLIGHT VALIDATION
                // =========================

                var validationResult =
                    await validation.EvaluateAsync(
                        mission,
                        "Preflight",
                        ct);

                if (!validationResult.IsValid)
                {
                    throw new MissionValidationException(
                        validationResult);
                }

                // =========================
                // STORE VALIDATION
                // =========================

                await validation.StoreAsync(
                    mission,
                    actor,
                    validationResult,
                    true,
                    ct);

                // =========================
                // PRE-FLIGHT ALERT
                // =========================

                await validation.SyncPreflightAlertAsync(
                    id,
                    validationResult,
                    ct);

                // =========================
                // START
                // =========================

                mission.Status = "InProgress";

                return validationResult;
            },
            cancellationToken);
    }
}