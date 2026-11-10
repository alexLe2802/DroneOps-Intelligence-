namespace DroneOps.Application.Settings;

public sealed class TelemetryMonitoringOptions
{
    public const string SectionName = "TelemetryMonitoring";

    public int LowBatteryPercent { get; set; } = 20;
    public int? LowBatteryCriticalPercent { get; set; }

    public decimal? MinimumAltitudeMslMeters { get; set; }
    public decimal? MaximumAltitudeMslMeters { get; set; }

    public int CommunicationLossSeconds { get; set; } = 10;

    public int ScanIntervalSeconds { get; set; } = 1;

    /// <summary>Tuổi tối đa của sample vẫn được coi là Online.</summary>
    public int StaleAfterSeconds { get; set; } = 10;

    /// <summary>Tuổi sample bắt đầu được coi là Offline; phải lớn hơn StaleAfterSeconds.</summary>
    public int OfflineAfterSeconds { get; set; } = 30;

    /// <summary>Khoảng hồi phục pin để tránh đóng/mở cảnh báo quanh đúng ngưỡng.</summary>
    public int LowBatteryRecoveryMarginPercent { get; set; } = 5;

}
