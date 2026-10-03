using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Pilot")]
public class Pilot
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public int ExperienceYears { get; set; }

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}