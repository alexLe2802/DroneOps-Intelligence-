namespace DroneOps.Application.DTOs.Response.Users;

public sealed class PilotUpdateRequestResponse
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public Guid UserId { get; set; }

    public string FullName { get; set; }
        = string.Empty;

    public string Email { get; set; }
        = string.Empty;

    public string PilotLicenseNo { get; set; }
        = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public string Status { get; set; }
        = string.Empty;

    public string? RejectReason { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public IReadOnlyList<string> LicenseImageUrls { get; set; }
        = Array.Empty<string>();
}