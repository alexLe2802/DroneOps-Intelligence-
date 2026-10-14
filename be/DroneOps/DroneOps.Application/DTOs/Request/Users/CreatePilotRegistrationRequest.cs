using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Request.Users;

public class CreatePilotRegistrationRequest
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string DroneType { get; set; } = string.Empty;

    [Required]
    public string PilotLicenseNo { get; set; } = string.Empty;

    [Required]
    public string UsagePurpose { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? DroneModel { get; set; }

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public int? ExperienceYears { get; set; }

    public string? Description { get; set; }
}