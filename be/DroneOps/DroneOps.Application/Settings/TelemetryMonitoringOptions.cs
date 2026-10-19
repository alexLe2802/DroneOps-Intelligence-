namespace DroneOps.Application.Settings;

public sealed class TelemetryMonitoringOptions
{
    public const string SectionName = "TelemetryMonitoring";

    public int LowBatteryPercent { get; set; } = 20;

    public decimal MaximumAltitudeMeters { get; set; } = 120;

    public decimal MaximumGroundSpeedMetersPerSecond { get; set; } = 25;

    public int CommunicationLossSeconds { get; set; } = 30;

    public int ScanIntervalSeconds { get; set; } = 5;
}
