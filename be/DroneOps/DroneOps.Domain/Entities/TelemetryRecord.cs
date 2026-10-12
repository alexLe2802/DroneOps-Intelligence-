using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Chỉ định ánh xạ chính xác vào bảng "TelemetryRecord" (số ít) trong CSDL
[Table("TelemetryRecord")]
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