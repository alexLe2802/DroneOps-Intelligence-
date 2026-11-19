namespace DroneOps.Application.DTOs.Response.License;

public sealed class PilotLicenseStatusResponse
{
    public Guid PilotId { get; set; }
    public Guid UserId { get; set; }
    public string PilotLicenseNo { get; set; } = string.Empty;
    public string LicenseStatus { get; set; } = string.Empty;
    public long LicenseVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PilotLicenseAuditResponse
{
    public Guid Id { get; set; }
    public Guid PilotId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
