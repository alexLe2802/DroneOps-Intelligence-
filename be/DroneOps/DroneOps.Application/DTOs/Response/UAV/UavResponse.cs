namespace DroneOps.Application.DTOs.Response.UAVs;

public sealed class UavResponse
{
    public DroneOps.Domain.Entities.FirmwareParameterSnapshot? FirmwareNavigationDefaults { get; set; }
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string PilotName { get; set; } = string.Empty;

    // Basic information
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public string Status { get; set; } = string.Empty;

    // Physical specifications
    public decimal? EmptyWeightKg { get; set; }

    public decimal? MaxTakeoffWeightKg { get; set; }

    public decimal? MaxPayloadKg { get; set; }

    // Battery specifications
    public string? BatteryType { get; set; }

    public int? BatteryCapacityMah { get; set; }

    public decimal? BatteryVoltage { get; set; } // Legacy nominal voltage; never reinterpret as full-charge voltage.
    public decimal? BatteryMaxVoltage { get; set; }

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

    // Media
    public string? ImageUrl { get; set; }

    // Audit
    public DateTimeOffset CreatedAt { get; set; }
}
