using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.Missions;

public class PendingApprovalItemResponse
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public Guid PilotId { get; set; }
    public string? PilotName { get; set; }
    public string? PilotEmail { get; set; }

    public Guid? UavId { get; set; }
    public string? UavCode { get; set; }
    public string? UavName { get; set; }

    public int WaypointCount { get; set; }

    /// <summary>
    /// Tóm tắt kiểm tra kế hoạch: Passed / Incomplete / Failed (không hard-code Passed).
    /// </summary>
    public string ValidationStatus { get; set; } = "Incomplete";

    public List<string> ValidationIssues { get; set; } = new();

    /// <summary>
    /// Phiên bản kế hoạch hiện tại dùng làm expectedVersion khi approve/reject.
    /// </summary>
    public int CurrentVersion { get; set; } = 1;
}

public class PagedPendingApprovalsResponse
{
    public List<PendingApprovalItemResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public class MissionDecisionResponse
{
    public Guid MissionId { get; set; }
    public string Decision { get; set; } = string.Empty; // Approved hoặc Rejected
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public Guid DecidedBy { get; set; }

    public bool IdempotentReplay { get; set; }

    public Guid? ValidationRunId { get; set; }
}
