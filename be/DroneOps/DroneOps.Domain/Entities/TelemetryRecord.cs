using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Ánh xạ vào bảng "TelemetryRecord" trong CSDL
[Table("TelemetryRecord")]
public class TelemetryRecord
{
    public Guid Id { get; set; }

    public string? SourceId { get; set; }

    public Guid UAVId { get; set; }

    public Guid? MissionId { get; set; }

    public DateTimeOffset SourceTimestamp { get; set; }

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    // Giữ RecordedAt để tương thích ngược 100% với module PostFlight hiện tại
    public DateTimeOffset RecordedAt { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public string? AltitudeReference { get; set; } = "Relative";

    public decimal? Speed { get; set; }

    public decimal? HeadingDegrees { get; set; }

    public int? BatteryLevel { get; set; }

    public decimal? BatteryVoltageVolts { get; set; }

    public decimal? BoardTemperatureCelsius { get; set; }

    public string? FlightMode { get; set; }

    public bool? Armed { get; set; }

    public int? MissionSequence { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool IsValid { get; set; } = true;

    // Navigation properties
    public Mission? Mission { get; set; }

    public UAV UAV { get; set; } = null!;
}
