namespace DroneOps.Domain.Entities;

public class Waypoint
{
    public Guid Id { get; set; }

    public Guid MissionVersionId { get; set; }

    public int SequenceOrder { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public string? ActionType { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public MissionVersion MissionVersion { get; set; } = null!;
}