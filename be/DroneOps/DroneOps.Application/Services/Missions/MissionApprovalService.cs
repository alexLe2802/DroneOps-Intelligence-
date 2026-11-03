using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionApprovalService(
    IMissionApprovalRepository approvalRepository,
    IMissionValidationService validation,
    AppDbContext db,
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

        await RepairMissingPendingRequestsAsync(ct);

        var query =
            from request in db.MissionUpdateRequests.AsNoTracking()
            join mission in db.Missions.AsNoTracking() on request.MissionId equals mission.Id
            where request.Status == "Pending" &&
                  (mission.Status == "Pending Review" || mission.Status == "Pending" ||
                   mission.Status == "PendingApproval" || mission.Status == "Submitted")
            select new { Request = request, Mission = mission };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.Mission.Name.Contains(search));
        }
        if (filter.UavId.HasValue) query = query.Where(x => x.Mission.UAVId == filter.UavId);
        if (filter.FromDate.HasValue) query = query.Where(x => x.Request.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.Request.CreatedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Request.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var missionIds = rows.Select(x => x.Mission.Id).ToArray();
        var missionsById = await db.Missions.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Waypoints)
            .Include(x => x.Creator)
            .Include(x => x.UAV)
            .Where(x => missionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var requestedRunIds = rows
            .Where(x => x.Request.ValidationRunId.HasValue)
            .Select(x => x.Request.ValidationRunId!.Value)
            .Distinct()
            .ToArray();
        var runsById = await db.MissionValidationRuns.AsNoTracking()
            .Where(x => requestedRunIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var fallbackMissionIds = rows
            .Where(x => !x.Request.ValidationRunId.HasValue)
            .Select(x => x.Mission.Id)
            .Distinct()
            .ToArray();
        var latestRuns = await db.MissionValidationRuns.AsNoTracking()
            .Where(x => x.MissionId.HasValue && fallbackMissionIds.Contains(x.MissionId.Value))
            .GroupBy(x => x.MissionId)
            .Select(group => group
                .OrderByDescending(x => x.CheckedAt)
                .ThenByDescending(x => x.Id)
                .First())
            .ToListAsync(ct);
        var latestRunsByMission = latestRuns
            .Where(x => x.MissionId.HasValue)
            .ToDictionary(x => x.MissionId!.Value);

        var items =
            new List<PendingApprovalItemResponse>();

        foreach (var row in rows)
        {
            if (!missionsById.TryGetValue(row.Mission.Id, out var mission))
            {
                logger?.LogWarning(
                    "Pending review request {RequestId} references a missing mission {MissionId}.",
                    row.Request.Id,
                    row.Mission.Id);
                continue;
            }
            CreateMissionRequest proposedDto;
            try
            {
                proposedDto = System.Text.Json.JsonSerializer.Deserialize<CreateMissionRequest>(
                    row.Request.ProposedPlanJson,
                    DroneOps.Application.Validators.Missions.MissionValidationService.JsonOptions)
                    ?? throw new System.Text.Json.JsonException("Proposed mission plan is missing.");
            }
            catch (System.Text.Json.JsonException ex) when (row.Request.IsInitial)
            {
                // Legacy backfills could persist nullable speed values even though
                // CreateMissionRequest uses non-nullable decimals. For an initial
                // submission the saved mission is the submitted plan, so it is the
                // safe canonical fallback.
                logger?.LogWarning(
                    ex,
                    "Using saved mission plan for legacy initial review request {RequestId}.",
                    row.Request.Id);
                proposedDto = MissionPlans.Dto(mission);
            }
            var proposed = MissionPlans.Propose(mission, proposedDto);
            MissionValidationRun? validationRun = null;
            if (row.Request.ValidationRunId.HasValue)
            {
                runsById.TryGetValue(row.Request.ValidationRunId.Value, out validationRun);
            }
            else
            {
                latestRunsByMission.TryGetValue(mission.Id, out validationRun);
            }
            var evidence =
                DroneOps.Application.Validators.Missions.MissionValidationService.Decode(validationRun);

            items.Add(
                new PendingApprovalItemResponse
                {
                    IsInitial = row.Request.IsInitial,
                    UpdateRequestId = row.Request.Id,
                    CurrentPlan = MissionPlans.Dto(mission),
                    ProposedPlan = MissionPlans.Dto(proposed),
                    MissionId = mission.Id,
                    MissionName = proposed.Name,
                    Description = proposed.Description,
                    Status = mission.Status,
                    CreatedAt = row.Request.CreatedAt.UtcDateTime,
                    StartTime = proposed.StartTime,
                    EndTime = proposed.EndTime,

                    PilotId = mission.CreatedBy,
                    PilotName =
                        mission.Creator?.FullName,
                    PilotEmail =
                        mission.Creator?.Email,

                    UavId = proposed.UAVId,
                    UavCode = mission.UAV?.Code,
                    UavName = mission.UAV?.Name,

                    WaypointCount =
                        proposed.Waypoints.Count,

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

    /// <summary>
    /// Repairs legacy/incomplete submissions that reached Pending Review before
    /// a MissionUpdateRequest was written. The approval queue is request-based,
    /// so leaving those rows incomplete makes valid missions invisible forever.
    /// </summary>
    private async Task RepairMissingPendingRequestsAsync(CancellationToken ct)
    {
        var pendingStatuses = new[] { "Pending Review", "Pending", "PendingApproval", "Submitted" };
        var missions = await db.Missions
            .Include(x => x.Waypoints)
            .Where(m => pendingStatuses.Contains(m.Status) &&
                        !db.MissionUpdateRequests.Any(r =>
                            r.MissionId == m.Id && r.Status == "Pending"))
            .ToListAsync(ct);

        foreach (var mission in missions)
        {
            var validationRun = await db.MissionValidationRuns
                .Where(x => x.MissionId == mission.Id)
                .OrderByDescending(x => x.CheckedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
            var requestId = Guid.NewGuid();

            db.MissionUpdateRequests.Add(new MissionUpdateRequest
            {
                Id = requestId,
                MissionId = mission.Id,
                ActorId = mission.CreatedBy,
                IsInitial = true,
                Status = "Pending",
                BasePlanHash = DroneOps.Application.Validators.Missions.MissionValidationService.Hash(mission),
                BasePlanJson = "{}",
                ProposedPlanJson = System.Text.Json.JsonSerializer.Serialize(
                    MissionPlans.Dto(mission),
                    DroneOps.Application.Validators.Missions.MissionValidationService.JsonOptions),
                ValidationRunId = validationRun?.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });

            if (validationRun is not null && validationRun.UpdateRequestId is null)
            {
                validationRun.UpdateRequestId = requestId;
            }
        }

        if (missions.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger?.LogWarning(
                "Repaired {Count} pending missions that were missing review requests.",
                missions.Count);
        }
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
     request.UpdateRequestId,
     request.ValidationRunId,
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
     request.UpdateRequestId,
     null,
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
Guid? updateRequestId,
Guid? reviewedValidationRunId,
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

                    var updateRequest = updateRequestId.HasValue
                        ? await db.MissionUpdateRequests.SingleOrDefaultAsync(
                            x => x.Id == updateRequestId.Value && x.MissionId == missionId,
                            transactionCt)
                        : await db.MissionUpdateRequests.SingleOrDefaultAsync(
                            x => x.MissionId == missionId && x.Status == "Pending",
                            transactionCt);

                    if (updateRequest is null)
                    {
                        throw new KeyNotFoundException("Mission review request not found.");
                    }
                    if (updateRequest.Status != "Pending")
                    {
                        throw new InvalidOperationException("This mission review request has already been processed.");
                    }

                    var proposedDto = System.Text.Json.JsonSerializer.Deserialize<CreateMissionRequest>(
                        updateRequest.ProposedPlanJson,
                        DroneOps.Application.Validators.Missions.MissionValidationService.JsonOptions)
                        ?? throw new InvalidOperationException("Proposed mission plan is missing.");
                    var proposedMission = MissionPlans.Propose(mission, proposedDto);

                    Guid? actualValidationRunId = null;

                    // =========================================
                    // APPROVE
                    // =========================================

                    if (decision == "Approved")
                    {
                        if (reviewedValidationRunId.HasValue)
                        {
                            var reviewedRun = await db.MissionValidationRuns
                                .SingleOrDefaultAsync(
                                    x => x.Id == reviewedValidationRunId.Value &&
                                         x.MissionId == missionId,
                                    transactionCt);

                            var proposalHash =
                                DroneOps.Application.Validators.Missions.MissionValidationService.Hash(
                                    proposedMission);

                            if (reviewedRun is null ||
                                reviewedRun.PlanHash != proposalHash ||
                                (reviewedRun.UpdateRequestId.HasValue &&
                                 reviewedRun.UpdateRequestId != updateRequest.Id))
                            {
                                throw new InvalidOperationException(
                                    "Reviewed validation does not belong to this request or proposal.");
                            }

                            // Legacy requests may have been backfilled after the
                            // validation was stored. Once mission ownership and the
                            // immutable plan hash match, repair both sides atomically.
                            reviewedRun.UpdateRequestId ??= updateRequest.Id;
                            updateRequest.ValidationRunId ??= reviewedRun.Id;
                        }

                        var freshValidation = await validation.EvaluateAsync(
                                proposedMission,
                                "Approval",
                                transactionCt);
                        freshValidation.UpdateRequestId = updateRequest.Id;

                        await validation.StoreAsync(
                            proposedMission,
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

                        db.Waypoints.RemoveRange(mission.Waypoints);
                        mission.Name = proposedMission.Name;
                        mission.Description = proposedMission.Description;
                        mission.UAVId = proposedMission.UAVId;
                        mission.AltitudeReference = proposedMission.AltitudeReference;
                        mission.StartTime = proposedMission.StartTime;
                        mission.EndTime = proposedMission.EndTime;
                        mission.AverageSpeedMetersPerSecond = proposedMission.AverageSpeedMetersPerSecond;
                        mission.MaxSpeedMetersPerSecond = proposedMission.MaxSpeedMetersPerSecond;
                        mission.Waypoints = proposedMission.Waypoints;
                        db.Waypoints.AddRange(proposedMission.Waypoints);

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

                        UpdateRequestId = updateRequest.Id,

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

                    updateRequest.Status = decision;
                    updateRequest.ReviewedBy = managerId;
                    updateRequest.ReviewedAt = decidedAt;
                    updateRequest.Reason = comment;
                    updateRequest.ValidationRunId = actualValidationRunId ?? updateRequest.ValidationRunId;

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
            UpdateRequestId = approval.UpdateRequestId,
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
