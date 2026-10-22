using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.PostFlight;

public class PostFlightReportResponse
{
    public Guid ReportId { get; set; }
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string? MissionDescription { get; set; }
    public string Status { get; set; } = string.Empty;
    public string GenerationStatus { get; set; } = string.Empty;
    public int Revision { get; set; }
    public bool IsCurrent { get; set; }
    public DateTimeOffset? CoveredTelemetryThrough { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? GenerationReason { get; set; }

    public string UavName { get; set; } = string.Empty;
    public string UavCode { get; set; } = string.Empty;
    public string PilotName { get; set; } = string.Empty;

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double TotalFlightDurationMinutes { get; set; }

    public decimal? MaxAltitude { get; set; }
    public decimal? AvgAltitude { get; set; }
    public decimal? MaxSpeed { get; set; }
    public decimal? AvgSpeed { get; set; }

    public int? InitialBatteryLevel { get; set; }
    public int? FinalBatteryLevel { get; set; }
    public int? BatteryConsumedPercentage { get; set; }
    public decimal? TotalDistanceMeters { get; set; }

    public int TotalTelemetryRecords { get; set; }
    public int TotalIncidents { get; set; }
    public string? SafetyEvaluation { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
