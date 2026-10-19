namespace DroneOps.Application.DTOs.Request.Users;

public sealed class UpdatePilotProfileRequest
{
    // Update trực tiếp vào Pilot
    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public int? ExperienceYears { get; set; }

    // Tạo PilotUpdateRequest Pending
    public string? PilotLicenseNo { get; set; }

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public List<string>? LicenseImageUrls { get; set; }
}