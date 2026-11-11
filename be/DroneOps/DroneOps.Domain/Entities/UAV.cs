using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("UAV")]
public class UAV
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    // Basic information
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }

    public string Status { get; set; } = "Pending";

    // Physical specifications
    public decimal? EmptyWeightKg { get; set; }
    public decimal? MaxTakeoffWeightKg { get; set; }
    public decimal? MaxPayloadKg { get; set; }

    // Battery specifications
    public string? BatteryType { get; set; }
    public int? BatteryCapacityMah { get; set; }
    public decimal? BatteryVoltage { get; set; } // Legacy nominal voltage; never reinterpret as full-charge voltage.
    public decimal? BatteryMaxVoltage { get; set; }
    public string? FirmwareNavigationDefaultsJson { get; set; }
    public int? BatteryCellCount { get; set; }

    // Flight capabilities
    public int? MaxFlightTimeMinutes { get; set; }
    public decimal? MaxRangeKm { get; set; }
    public decimal? MaxSpeedKmh { get; set; }
    public decimal? MaxWindSpeedMps { get; set; }
    public decimal? MaxOperatingAltitudeMeters { get; set; }

    // Operational information
    public DateTimeOffset? PurchaseDate { get; set; }
    public DateTimeOffset? LastMaintenanceAt { get; set; }
    public DateTimeOffset? NextMaintenanceAt { get; set; }

    public decimal TotalFlightHours { get; set; }
    public int TotalFlights { get; set; }

    // Image
    public string? ImageStoragePath { get; set; }
    public string? ImageContentType { get; set; }
    public DateTimeOffset? ImageUpdatedAt { get; set; }
    public string? ImageModerationStatus { get; set; }
    public int ImageModerationAttempts { get; set; }
    public DateTimeOffset? ImageModerationNextAttemptAt { get; set; }
    public DateTimeOffset? ImageModeratedAt { get; set; }
    public string? ImageModerationReason { get; set; }

    // Audit
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Pilot Pilot { get; set; } = null!;
}
