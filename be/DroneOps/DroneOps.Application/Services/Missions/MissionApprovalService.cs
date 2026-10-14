using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Missions;

public class MissionApprovalService : IMissionApprovalService
{
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
        var query = _context.Missions
            .AsNoTracking()
            .Include(m => m.Creator)
            .Include(m => m.UAV)
            .Include(m => m.Waypoints)
            .Where(m => m.Status == "Pending Review" || m.Status == "Pending");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var cleanSearch = request.Search.Trim().ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(cleanSearch) ||
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
        var pageSize = request.PageSize <= 0 ? 10 : (request.PageSize > 100 ? 100 : request.PageSize);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new PendingApprovalItemResponse
            {
                MissionId = m.Id,
                MissionName = m.Name,
                Description = m.Description,
                Status = m.Status,
                CreatedAt = m.CreatedAt,
                StartTime = m.StartTime,
                EndTime = m.EndTime,
                PilotId = m.CreatedBy,
                PilotName = m.Creator != null ? m.Creator.FullName : null,
                PilotEmail = m.Creator != null ? m.Creator.Email : null,
                UavId = m.UAVId,
                UavCode = m.UAV != null ? m.UAV.Code : null,
                UavName = m.UAV != null ? m.UAV.Name : null,
                WaypointCount = m.Waypoints.Count,
                ValidationStatus = "Passed"
            })
            .ToListAsync(cancellationToken);

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
        var mission = await _context.Missions
            .Include(m => m.Waypoints)
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null)
        {
            throw new KeyNotFoundException($"Mission with ID {missionId} not found.");
        }

        // Quy tắc D-03: Không tự duyệt nhiệm vụ do chính mình tạo
        if (mission.CreatedBy == managerId)
        {
            throw new InvalidOperationException("Self-approval is forbidden. A manager cannot approve their own mission plan.");
        }

        // Kiểm tra trạng thái hợp lệ để duyệt
        if (mission.Status != "Pending Review" && mission.Status != "Pending")
        {
            throw new InvalidOperationException($"Cannot approve mission with status '{mission.Status}'. Mission must be in 'Pending Review' state.");
        }

        if (!mission.Waypoints.Any())
        {
            throw new ArgumentException("Cannot approve mission with no waypoints defined.");
        }

        var prevStatus = mission.Status;
        var now = DateTimeOffset.UtcNow;

        mission.Status = "Approved";

        var approvalLog = new MissionApproval
        {
            Id = Guid.NewGuid(),
            MissionId = mission.Id,
            UserId = managerId,
            Status = "Approved",
            Comment = request.Comment,
            ApprovedAt = now
        };

        await _context.MissionApprovals.AddAsync(approvalLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new MissionDecisionResponse
        {
            MissionId = mission.Id,
            Decision = "Approved",
            PreviousStatus = prevStatus,
            NewStatus = mission.Status,
            Comment = request.Comment,
            DecidedAt = now,
            DecidedBy = managerId
        };
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

        var mission = await _context.Missions
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null)
        {
            throw new KeyNotFoundException($"Mission with ID {missionId} not found.");
        }

        if (mission.Status != "Pending Review" && mission.Status != "Pending")
        {
            throw new InvalidOperationException($"Cannot reject mission with status '{mission.Status}'. Mission must be in 'Pending Review' state.");
        }

        var prevStatus = mission.Status;
        var now = DateTimeOffset.UtcNow;

        mission.Status = "Rejected";

        var approvalLog = new MissionApproval
        {
            Id = Guid.NewGuid(),
            MissionId = mission.Id,
            UserId = managerId,
            Status = "Rejected",
            Comment = request.Reason.Trim(),
            ApprovedAt = now
        };

        await _context.MissionApprovals.AddAsync(approvalLog, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new MissionDecisionResponse
        {
            MissionId = mission.Id,
            Decision = "Rejected",
            PreviousStatus = prevStatus,
            NewStatus = mission.Status,
            Comment = request.Reason.Trim(),
            DecidedAt = now,
            DecidedBy = managerId
        };
    }
}