namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionResponse
{
    public MissionUpdateSummary? PendingUpdate { get; set; }
    public string? AltitudeReference { get; set; }

    public MissionValidationResult? PreflightValidation { get; set; }
    public MissionValidationResult? Validation { get; set; }
    public MissionValidationResult? LatestValidation { get; set; }
    public string FlightReadiness { get; set; } = "AwaitingPreflight";

    public Guid Id { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? UavId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }
    public decimal? AverageSpeedMetersPerSecond { get; set; }

    public decimal? MaxSpeedMetersPerSecond { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<WaypointResponse> Waypoints { get; set; } = [];
}
public sealed class MissionUpdateSummary
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "Pending";
    public bool IsInitial { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid ActorId { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Reason { get; set; }
    public DroneOps.Application.DTOs.Request.Missions.CreateMissionRequest? BasePlan { get; set; }
    public DroneOps.Application.DTOs.Request.Missions.CreateMissionRequest ProposedPlan { get; set; } = new();
    public MissionValidationResult? Validation { get; set; }
}
