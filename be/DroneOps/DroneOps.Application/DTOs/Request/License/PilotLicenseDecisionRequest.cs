namespace DroneOps.Application.DTOs.Request.License;

public sealed class PilotLicenseDecisionRequest
{
    public long? ExpectedVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}
