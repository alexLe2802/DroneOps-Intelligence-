namespace DroneOps.Application.Settings;

public sealed class TelemetryMonitoringOptions
{
    public const string SectionName = "TelemetryMonitoring";

    public int LowBatteryPercent { get; set; } = 20;

    public decimal MaximumAltitudeMeters { get; set; } = 120;

    public decimal MaximumGroundSpeedMetersPerSecond { get; set; } = 25;

    public int CommunicationLossSeconds { get; set; } = 30;

    public int ScanIntervalSeconds { get; set; } = 5;

    /// <summary>Tuổi tối đa của sample vẫn được coi là Online.</summary>
    public int StaleAfterSeconds { get; set; } = 10;

    /// <summary>Tuổi sample bắt đầu được coi là Offline; phải lớn hơn StaleAfterSeconds.</summary>
    public int OfflineAfterSeconds { get; set; } = 30;
}
