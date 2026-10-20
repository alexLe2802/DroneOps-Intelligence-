namespace DroneOps.Application.DTOs.Request.Users;

public sealed class RejectPilotUpdateRequest
{
    public string RejectReason { get; set; }
        = string.Empty;
}