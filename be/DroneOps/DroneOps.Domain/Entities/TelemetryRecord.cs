namespace DroneOps.Domain.Entities;

public class TelemetryRecord
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public Guid UAVId { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public decimal? Speed { get; set; }

    public int? BatteryLevel { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Mission Mission { get; set; } = null!;

    public UAV UAV { get; set; } = null!;
}