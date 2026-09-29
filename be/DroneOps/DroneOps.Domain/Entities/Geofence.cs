namespace DroneOps.Domain.Entities;

public class Geofence
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Coordinates { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}