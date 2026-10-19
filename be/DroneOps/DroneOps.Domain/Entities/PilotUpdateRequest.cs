using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("PilotUpdateRequest")]
public class PilotUpdateRequest
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string PilotLicenseNo { get; set; } = string.Empty;

    public DateOnly? LicenseIssuedDate { get; set; }

    public DateOnly? LicenseExpiredDate { get; set; }

    public string Status { get; set; } = "Pending";

    public string? RejectReason { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Pilot Pilot { get; set; } = null!;

    public ICollection<LicenseImage> LicenseImages { get; set; }
        = new List<LicenseImage>();
}