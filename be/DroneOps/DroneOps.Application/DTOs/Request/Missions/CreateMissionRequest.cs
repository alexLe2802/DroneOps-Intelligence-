namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class CreateMissionRequest
{
    public Guid UavId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public List<CreateWaypointRequest> Waypoints { get; set; } = [];
}
