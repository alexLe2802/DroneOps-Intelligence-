using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.Missions;

public class PendingApprovalItemResponse
{
    public bool RequiresPilotConfirmation { get; set; }
    public Guid ProposalAuthorId { get; set; }
    public bool IsInitial { get; set; }
    public DroneOps.Application.DTOs.Request.Missions.CreateMissionRequest? CurrentPlan { get; set; }
    public DroneOps.Application.DTOs.Request.Missions.CreateMissionRequest? ProposedPlan { get; set; }
    public MissionValidationResult? Validation { get; set; }
    public Guid? UpdateRequestId { get; set; }
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
    /// Tóm tắt kiểm tra kế hoạch: Pass / Fail / Incomplete / Error.
    /// </summary>
    public string ValidationStatus { get; set; } = "Incomplete";

    public List<string> ValidationIssues { get; set; } = new();

    public PilotApprovalTrackRecord? PilotRecord { get; set; }

    public ApprovalUavSummary? AssignedUav { get; set; }
}

public class PilotApprovalTrackRecord
{
    public int CompletedMissionsCount { get; set; }
    public int TotalIncidentsCount { get; set; }
    public int OpenIncidentsCount { get; set; }
    public List<PilotCompletedMissionSummary> RecentCompletedMissions { get; set; } = new();
}

public class PilotCompletedMissionSummary
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string? UavCode { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int IncidentCount { get; set; }
}

public class ApprovalUavSummary
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool HasPendingProposal { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public decimal? EmptyWeightKg { get; set; }
    public decimal? MaxTakeoffWeightKg { get; set; }
    public decimal? MaxPayloadKg { get; set; }
    public string? BatteryType { get; set; }
    public int? BatteryCapacityMah { get; set; }
    public decimal? BatteryVoltage { get; set; }
    public int? BatteryCellCount { get; set; }
    public int? MaxFlightTimeMinutes { get; set; }
    public decimal? MaxRangeKm { get; set; }
    public decimal? MaxSpeedKmh { get; set; }
    public decimal? MaxWindSpeedMps { get; set; }
    public decimal? MaxOperatingAltitudeMeters { get; set; }
    public DateTimeOffset? LastMaintenanceAt { get; set; }
    public DateTimeOffset? NextMaintenanceAt { get; set; }
    public decimal TotalFlightHours { get; set; }
    public int TotalFlights { get; set; }
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
    public Guid? UpdateRequestId { get; set; }
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
