namespace DroneOps.Application.DTOs.Response.Users;

public class PilotRegistrationDetailResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string DroneType { get; set; } = string.Empty;

    public string? DroneModel { get; set; }

    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public int? ExperienceYears { get; set; }

    public string UsagePurpose { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? RejectReason { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}