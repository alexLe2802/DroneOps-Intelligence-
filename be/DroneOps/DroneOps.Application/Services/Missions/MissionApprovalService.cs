using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionApprovalService(
    IMissionApprovalRepository approvalRepository,
    IMissionValidationService validation,
    INotificationService? notificationService = null,
    ILogger<MissionApprovalService>? logger = null)
    : IMissionApprovalService
{
    // =========================================================
    // GET PENDING APPROVALS
    // =========================================================

    public async Task<PagedPendingApprovalsResponse>
        GetPendingApprovalsAsync(
            GetPendingApprovalsRequest filter,
            Guid managerId,
            CancellationToken ct = default)
    {
        var page = Math.Max(
            1,
            filter.PageNumber);

        var size = Math.Clamp(
            filter.PageSize,
            1,
            100);

        var (missions, totalCount) =
            await approvalRepository
                .GetPendingApprovalsAsync(
                    filter.Search,
                    filter.UavId,
                    filter.FromDate,
                    filter.ToDate,
                    page,
                    size,
                    ct);

        var items =
            new List<PendingApprovalItemResponse>();

        foreach (var mission in missions)
        {
            var evidence =
                await validation.ReadAsync(
                    mission.Id,
                    managerId,
                    true,
                    ct);

            items.Add(
                new PendingApprovalItemResponse
                {
                    MissionId = mission.Id,
                    MissionName = mission.Name,
                    Description = mission.Description,
                    Status = mission.Status,
                    CreatedAt = mission.CreatedAt,
                    StartTime = mission.StartTime,
                    EndTime = mission.EndTime,

                    PilotId = mission.CreatedBy,
                    PilotName =
                        mission.Creator?.FullName,
                    PilotEmail =
                        mission.Creator?.Email,

                    UavId = mission.UAVId,
                    UavCode = mission.UAV?.Code,
                    UavName = mission.UAV?.Name,

                    WaypointCount =
                        mission.Waypoints.Count,

                    Validation = evidence,

                    ValidationStatus =
                        evidence?.Status
                        ?? "Incomplete",

                    ValidationIssues =
                        evidence?.Issues
                        ?? ["Revalidate this mission."]
                });
        }

        return new PagedPendingApprovalsResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = size
        };
    }

    // =========================================================
    // APPROVE
    // =========================================================

    public async Task<MissionDecisionResponse>
        ApproveMissionAsync(
            Guid id,
            ApproveMissionRequest request,
            Guid manager,
            CancellationToken ct = default)
    {
        var response = await DecideAsync(
     id,
     manager,
     "Approved",
     request.Comment,
     ct);

        await TryPublishDecisionNotificationAsync(
            response,
            manager,
            true,
            request.Comment,
            ct);

        return response;
    }

    // =========================================================
    // REJECT
    // =========================================================

    public async Task<MissionDecisionResponse>
        RejectMissionAsync(
            Guid id,
            RejectMissionRequest request,
            Guid manager,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException(
                "Rejection reason is mandatory.");
        }

        var reason = request.Reason.Trim();

        var response = await DecideAsync(
     id,
     manager,
     "Rejected",
     reason,
     ct);

        await TryPublishDecisionNotificationAsync(
            response,
            manager,
            false,
            reason,
            ct);

        return response;
    }

    // =========================================================
    // DECISION
    // =========================================================

    private async Task<MissionDecisionResponse> DecideAsync(
      Guid missionId,
Guid managerId,
string decision,
string? comment,
CancellationToken ct)
    {
        var outcome =
            await approvalRepository.ExecuteSerializableAsync(
                async transactionCt =>
                {
                    var mission =
                        await approvalRepository
                            .GetByIdWithDetailsAsync(
                                missionId,
                                true,
                                transactionCt)
                        ?? throw new KeyNotFoundException(
                            "Mission not found.");

                    // Manager cannot approve/reject own Mission.
                    if (mission.CreatedBy == managerId)
                    {
                        throw new InvalidOperationException(
                            "Self-approval is forbidden.");
                    }

                    // Idempotent replay.
                    if (mission.Status == decision)
                    {
                        var previousDecision =
                            await approvalRepository
                                .GetLatestDecisionAsync(
                                    missionId,
                                    managerId,
                                    decision,
                                    transactionCt);

                        if (previousDecision is not null)
                        {
                            return DecisionOutcome.Success(
                                BuildResponse(
                                    previousDecision,
                                    mission.Status,
                                    mission.Status,
                                    true));
                        }
                    }

                    if (mission.Status is
                        "Cancelled" or
                        "Completed" or
                        "InProgress" or
                        "In Progress")
                    {
                        throw new InvalidOperationException(
                            "Mission is no longer eligible for review.");
                    }

                    if (mission.Status != "Pending Review")
                    {
                        throw new InvalidOperationException(
                            "Only missions pending review can be approved or rejected.");
                    }

                    var previousStatus = mission.Status;

                    Guid? actualValidationRunId = null;

                    // =========================================
                    // APPROVE
                    // =========================================

                    if (decision == "Approved")
                    {
                        var freshValidation =
                            await validation.EvaluateAsync(
                                mission,
                                "Approval",
                                transactionCt);

                        await validation.StoreAsync(
                            mission,
                            managerId,
                            freshValidation,
                            true,
                            transactionCt);

                        actualValidationRunId =
                            freshValidation.Id;

                        if (!freshValidation.IsValid)
                        {
                            return DecisionOutcome.ValidationFailed(
                                freshValidation);
                        }

                        mission.Status = "Approved";
                    }
                    else
                    {
                        mission.Status = "Rejected";
                    }

                    // =========================================
                    // AUDIT
                    // =========================================

                    var decidedAt =
                        DateTimeOffset.UtcNow;

                    var approval = new MissionApproval
                    {
                        Id = Guid.NewGuid(),
                        MissionId = mission.Id,

                        UpdateRequestId = null,

                        UserId = managerId,
                        Status = decision,
                        Comment = comment,
                        ApprovedAt = decidedAt,

                        ValidationRunId =
           decision == "Approved"
               ? actualValidationRunId
               : null
                    };

                    await approvalRepository.AddApprovalAsync(
                        approval,
                        transactionCt);

                    var response = BuildResponse(
                        approval,
                        previousStatus,
                        mission.Status,
                        false);

                    return DecisionOutcome.Success(response);
                },
                ct);

        if (outcome.ValidationFailure is not null)
        {
            throw new MissionValidationException(
                outcome.ValidationFailure);
        }

        return outcome.Response
            ?? throw new InvalidOperationException(
                "Mission decision result is missing.");
    }

    // =========================================================
    // RESPONSE
    // =========================================================

    private static MissionDecisionResponse BuildResponse(
    MissionApproval approval,
    string previousStatus,
    string newStatus,
    bool replay)
    {
        return new MissionDecisionResponse
        {
            MissionId = approval.MissionId,
            Decision = approval.Status,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Comment = approval.Comment,

            DecidedAt =
                approval.ApprovedAt
                ?? DateTimeOffset.UtcNow,

            DecidedBy = approval.UserId,
            IdempotentReplay = replay,
            ValidationRunId = approval.ValidationRunId
        };
    }

    // =========================================================
    // NOTIFICATION
    // =========================================================

    private async Task TryPublishDecisionNotificationAsync(
        MissionDecisionResponse response,
        Guid actorUserId,
        bool approved,
        string? reason,
        CancellationToken ct)
    {
        if (response.IdempotentReplay ||
            notificationService is null)
        {
            return;
        }

        try
        {
            await notificationService
                .PublishMissionDecisionAsync(
                    response.MissionId,
                    actorUserId,
                    approved,
                    reason,
                    ct);
        }
        catch (Exception ex)
            when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(
                ex,
                "Could not publish mission-decision notification for {MissionId}.",
                response.MissionId);
        }
    }

    // =========================================================
    // INTERNAL TRANSACTION RESULT
    // =========================================================

    private sealed class DecisionOutcome
    {
        public MissionDecisionResponse? Response { get; init; }

        public MissionValidationResult? ValidationFailure { get; init; }

        public static DecisionOutcome Success(
            MissionDecisionResponse response)
        {
            return new DecisionOutcome
            {
                Response = response
            };
        }

        public static DecisionOutcome ValidationFailed(
            MissionValidationResult validationResult)
        {
            return new DecisionOutcome
            {
                ValidationFailure = validationResult
            };
        }
    }
}