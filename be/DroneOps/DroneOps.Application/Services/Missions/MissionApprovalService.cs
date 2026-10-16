using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Missions;

public class MissionApprovalService : IMissionApprovalService
{
    // Các trạng thái được coi là "đang chờ duyệt" — FE/legacy DB có thể dùng tên khác nhau.
    private static readonly string[] PendingStatuses =
    {
        "Pending Review",
        "Pending",
        "PendingApproval"
    };

    private const int CurrentPlanVersion = 1;

    private readonly AppDbContext _context;

    public MissionApprovalService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedPendingApprovalsResponse> GetPendingApprovalsAsync(
        GetPendingApprovalsRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        // Manager xem toàn bộ hàng đợi; không trả bản Draft/Approved.
        var query = _context.Missions
            .AsNoTracking()
            .Where(m => PendingStatuses.Contains(m.Status));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var cleanSearch = request.Search.Trim().ToLower();
            query = query.Where(m =>
                m.Name.ToLower().Contains(cleanSearch) ||
                (m.Description != null && m.Description.ToLower().Contains(cleanSearch)));
        }

        if (request.UavId.HasValue)
        {
            query = query.Where(m => m.UAVId == request.UavId.Value);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt <= request.ToDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 10 : Math.Min(request.PageSize, 100);

        // Select trực tiếp — Count waypoint ở SQL, không Include cả collection.
        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Description,
                m.Status,
                m.CreatedAt,
                m.StartTime,
                m.EndTime,
                m.CreatedBy,
                PilotName = m.Creator != null ? m.Creator.FullName : null,
                PilotEmail = m.Creator != null ? m.Creator.Email : null,
                m.UAVId,
                UavCode = m.UAV != null ? m.UAV.Code : null,
                UavName = m.UAV != null ? m.UAV.Name : null,
                WaypointCount = m.Waypoints.Count
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(m =>
        {
            var issues = EvaluatePlanIssues(m.UAVId, m.WaypointCount, m.StartTime, m.EndTime);
            return new PendingApprovalItemResponse
            {
                MissionId = m.Id,
                MissionName = m.Name,
                Description = m.Description,
                Status = m.Status,
                CreatedAt = DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc),
                StartTime = m.StartTime,
                EndTime = m.EndTime,
                PilotId = m.CreatedBy,
                PilotName = m.PilotName,
                PilotEmail = m.PilotEmail,
                UavId = m.UAVId,
                UavCode = m.UavCode,
                UavName = m.UavName,
                WaypointCount = m.WaypointCount,
                ValidationStatus = ToValidationStatus(issues),
                ValidationIssues = issues,
                CurrentVersion = CurrentPlanVersion
            };
        }).ToList();

        return new PagedPendingApprovalsResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<MissionDecisionResponse> ApproveMissionAsync(
        Guid missionId,
        ApproveMissionRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        return await DecideAsync(
            missionId,
            managerId,
            decision: "Approved",
            comment: request.Comment,
            expectedStatus: request.ExpectedStatus,
            expectedVersion: request.ExpectedVersion,
            validationRunId: request.ValidationRunId,
            cancellationToken);
    }

    public async Task<MissionDecisionResponse> RejectMissionAsync(
        Guid missionId,
        RejectMissionRequest request,
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Rejection reason is mandatory.");
        }

        return await DecideAsync(
            missionId,
            managerId,
            decision: "Rejected",
            comment: request.Reason.Trim(),
            expectedStatus: request.ExpectedStatus,
            expectedVersion: request.ExpectedVersion,
            validationRunId: null,
            cancellationToken);
    }

    private async Task<MissionDecisionResponse> DecideAsync(
        Guid missionId,
        Guid managerId,
        string decision,
        string? comment,
        string? expectedStatus,
        int? expectedVersion,
        Guid? validationRunId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var mission = await _context.Missions
            .Include(m => m.Waypoints)
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null)
        {
            throw new KeyNotFoundException($"Mission with ID {missionId} not found.");
        }

        // D-03: manager không được tự duyệt / tự từ chối kế hoạch do chính mình tạo.
        if (mission.CreatedBy == managerId)
        {
            throw new InvalidOperationException(
                "Self-approval is forbidden. A manager cannot decide on their own mission plan.");
        }

        if (expectedVersion.HasValue && expectedVersion.Value != CurrentPlanVersion)
        {
            throw new InvalidOperationException(
                $"Stale plan version. Expected version {expectedVersion.Value} but current version is {CurrentPlanVersion}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedStatus) &&
            !StatusesMatch(expectedStatus, mission.Status))
        {
            throw new InvalidOperationException(
                $"Stale decision: expected status '{expectedStatus}' but current status is '{mission.Status}'.");
        }

        // Retry/idempotency: cùng manager, cùng quyết định đã ghi nhận -> trả kết quả cũ, không nhân bản audit.
        var existingLog = await _context.MissionApprovals
            .AsNoTracking()
            .Where(a => a.MissionId == missionId)
            .OrderByDescending(a => a.ApprovedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (!IsPendingStatus(mission.Status))
        {
            if (existingLog != null &&
                existingLog.UserId == managerId &&
                string.Equals(existingLog.Status, decision, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ToDecisionResponse(mission.Id, existingLog, mission.Status, idempotent: true, validationRunId);
            }

            throw new InvalidOperationException(
                $"Cannot {decision.ToLowerInvariant()} mission with status '{mission.Status}'. Mission must be in 'Pending Review' state.");
        }

        if (string.Equals(decision, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            var issues = EvaluatePlanIssues(mission.UAVId, mission.Waypoints.Count, mission.StartTime, mission.EndTime);
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    "Cannot approve mission until mandatory validation passes: " + string.Join("; ", issues));
            }
        }

        var previousStatus = mission.Status;
        var now = DateTimeOffset.UtcNow;
        mission.Status = decision;

        var approvalLog = new MissionApproval
        {
            Id = Guid.NewGuid(),
            MissionId = mission.Id,
            UserId = managerId,
            Status = decision,
            Comment = comment,
            ApprovedAt = now
        };

        await _context.MissionApprovals.AddAsync(approvalLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MissionDecisionResponse
        {
            MissionId = mission.Id,
            Decision = decision,
            PreviousStatus = previousStatus,
            NewStatus = mission.Status,
            Comment = comment,
            DecidedAt = now,
            DecidedBy = managerId,
            IdempotentReplay = false,
            ValidationRunId = validationRunId
        };
    }

    private static MissionDecisionResponse ToDecisionResponse(
        Guid missionId,
        MissionApproval log,
        string currentStatus,
        bool idempotent,
        Guid? validationRunId)
    {
        return new MissionDecisionResponse
        {
            MissionId = missionId,
            Decision = log.Status,
            PreviousStatus = log.Status,
            NewStatus = currentStatus,
            Comment = log.Comment,
            DecidedAt = log.ApprovedAt ?? DateTimeOffset.UtcNow,
            DecidedBy = log.UserId,
            IdempotentReplay = idempotent,
            ValidationRunId = validationRunId
        };
    }

    private static bool IsPendingStatus(string status) =>
        PendingStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    private static bool StatusesMatch(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsPendingStatus(expected) && IsPendingStatus(actual);
    }

    private static List<string> EvaluatePlanIssues(
        Guid? uavId,
        int waypointCount,
        DateTime? startTime,
        DateTime? endTime)
    {
        var issues = new List<string>();

        if (!uavId.HasValue || uavId.Value == Guid.Empty)
        {
            issues.Add("UAV assignment is missing.");
        }

        if (waypointCount <= 0)
        {
            issues.Add("At least one waypoint is required.");
        }

        if (!startTime.HasValue)
        {
            issues.Add("Scheduled start time is missing.");
        }

        if (startTime.HasValue && endTime.HasValue && endTime <= startTime)
        {
            issues.Add("End time must be after start time.");
        }

        return issues;
    }

    private static string ToValidationStatus(List<string> issues)
    {
        if (issues.Count == 0)
        {
            return "Passed";
        }

        return issues.Exists(i => i.Contains("waypoint", StringComparison.OrdinalIgnoreCase))
            ? "Incomplete"
            : "Failed";
    }
}
