namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionResponse
{
    public Guid Id { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? UavId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public DateTime CreatedAt { get; set; }

    public int VersionNumber { get; set; }

    public List<WaypointResponse> Waypoints { get; set; } = [];
}