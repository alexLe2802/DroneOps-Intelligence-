namespace DroneOps.Application.DTOs.Response.Users;

public sealed class UpdatePilotProfileResponse
{
    public Guid UserId { get; set; }

    public Guid PilotId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public int ExperienceYears { get; set; }

    // License chính thức hiện tại
    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    // Request cập nhật mới, nếu có
    public Guid? PilotUpdateRequestId { get; set; }

    public string? PilotUpdateRequestStatus { get; set; }
}