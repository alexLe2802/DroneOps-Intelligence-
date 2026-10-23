namespace DroneOps.Application.DTOs.Response.Users;

public sealed class OperatorCredentialsResponse
{
    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public int ExperienceYears { get; set; }

    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public List<string> LicenseImageUrls { get; set; } = [];

    public bool DocumentsAvailable { get; set; }
}