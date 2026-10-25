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

    /// <summary>Khoảng hồi phục pin để tránh đóng/mở cảnh báo quanh đúng ngưỡng.</summary>
    public int LowBatteryRecoveryMarginPercent { get; set; } = 5;

    public decimal AltitudeRecoveryMarginMeters { get; set; } = 5;

    public decimal GroundSpeedRecoveryMarginMetersPerSecond { get; set; } = 3;
}
