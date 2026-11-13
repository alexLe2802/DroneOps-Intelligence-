namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class CreateMissionRequest
{
    public Guid? OperationId { get; set; }
    public string? ExpectedPlanHash { get; set; }
    public Guid? ExpectedPendingRequestId { get; set; }
    public string? AltitudeReference { get; set; }
    public bool AutomaticFlightRequested { get; set; }
    public DroneOps.Domain.Entities.AutomaticFlightLimits? AutomaticFlightLimits { get; set; }

    public Guid UavId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
    public decimal AverageSpeedMetersPerSecond { get; set; } = 11.2m;

    public decimal MaxSpeedMetersPerSecond { get; set; } = 15m;
    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public List<CreateWaypointRequest> Waypoints { get; set; } = [];
}
