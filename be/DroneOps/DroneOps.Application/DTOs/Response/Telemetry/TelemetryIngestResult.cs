namespace DroneOps.Application.DTOs.Response.Telemetry;

/// <summary>
/// Kết quả nạp mẫu viễn trắc: phân biệt gói mới, gói trùng (idempotent) và gói đến muộn.
/// </summary>
public class TelemetryIngestResult
{
    public bool Accepted { get; set; }

    public bool IsDuplicate { get; set; }

    public bool IsLateArrival { get; set; }

    public bool UsedForLatestProjection { get; set; }

    public Guid? RecordId { get; set; }

    public Guid? RaisedIncidentId { get; set; }

    public string Message { get; set; } = string.Empty;
}
