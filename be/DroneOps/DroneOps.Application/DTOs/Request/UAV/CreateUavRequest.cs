using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Request.UAVs;

public sealed class CreateUavRequest
{
    // Basic information
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Manufacturer { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    // Physical specifications
    [Range(0.001, double.MaxValue)]
    public decimal? EmptyWeightKg { get; set; }

    [Range(0.001, double.MaxValue)]
    public decimal? MaxTakeoffWeightKg { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MaxPayloadKg { get; set; }

    // Battery specifications
    [MaxLength(50)]
    public string? BatteryType { get; set; }

    [Range(1, int.MaxValue)]
    public int? BatteryCapacityMah { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? BatteryVoltage { get; set; }

    [Range(1, 100)]
    public int? BatteryCellCount { get; set; }

    // Flight capabilities
    [Range(1, int.MaxValue)]
    public int? MaxFlightTimeMinutes { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MaxRangeKm { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MaxSpeedKmh { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MaxWindSpeedMps { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MaxOperatingAltitudeMeters { get; set; }

    // Operational information
    public DateTimeOffset? PurchaseDate { get; set; }
}