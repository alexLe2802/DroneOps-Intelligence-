namespace DroneOps.Application.DTOs.Response.Users;

public class PilotRegistrationResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string DroneType { get; set; } = string.Empty;

    public string? DroneModel { get; set; }

    public string PilotLicenseNo { get; set; } = string.Empty;

    public string UsagePurpose { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}