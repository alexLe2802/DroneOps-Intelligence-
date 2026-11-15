namespace DroneOps.Application.DTOs.Response.AI;

public sealed class BatteryAssessmentResponse
{
    public string Status { get; set; } = "MissingData";
    public string Model { get; set; } = "route-endurance-weather-v1";
    public string CapacitySource { get; set; } = "UAV configuration; not reported by the flight controller";
    public int? ConfiguredCapacityMah { get; set; }
    public int? CurrentBatteryPercent { get; set; }
    public int? ConsumedMah { get; set; }
    public DateTimeOffset? TelemetrySourceTimestamp { get; set; }
    public decimal? EstimatedRemainingChargeMah { get; set; }
    public decimal? RequiredBatteryPercent { get; set; }
    public decimal? EstimatedRequiredChargeMah { get; set; }
    public bool? MeetsEstimatedRequirement { get; set; }
    public double? RouteDistanceMeters { get; set; }
    public decimal? EstimatedFlightMinutes { get; set; }
    public decimal? BaseBatteryPercent { get; set; }
    public decimal? WeatherAdjustmentPercent { get; set; }
    public decimal? SafetyReservePercent { get; set; }
    public List<string> Reasons { get; set; } = [];
    public List<string> MissingData { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
