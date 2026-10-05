namespace DroneOps.Domain.Entities;

public class MissionVersion
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public int VersionNumber { get; set; }

    public string Status { get; set; } = "Draft";

    public DateTimeOffset CreatedAt { get; set; }

    public Mission Mission { get; set; } = null!;

    public ICollection<Waypoint> Waypoints { get; set; }
        = new List<Waypoint>();
}