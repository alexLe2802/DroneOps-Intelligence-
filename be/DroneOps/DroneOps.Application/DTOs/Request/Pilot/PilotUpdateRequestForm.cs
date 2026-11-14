using Microsoft.AspNetCore.Http;

namespace DroneOps.Application.DTOs.Request.Users;

public sealed class PilotUpdateRequestForm
{
    public Guid? ExpectedPendingRequestId { get; set; }
    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public List<IFormFile> Files { get; set; } = [];
}
