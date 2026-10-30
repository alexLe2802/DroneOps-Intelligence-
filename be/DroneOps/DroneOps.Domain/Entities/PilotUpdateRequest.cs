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

[Table("PilotUpdateDecisionAudit")]
public sealed class PilotUpdateDecisionAudit
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorId { get; set; }
    public Guid TargetUserId { get; set; }
    public string Result { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}

[Table("EmailChangeChallenge",Schema="droneops")]
public sealed class EmailChangeChallenge
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string NewEmail { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string Status { get; set; } = "Pending";
}

[Table("AccountWorkflowAudit",Schema="droneops")]
public sealed class AccountWorkflowAudit
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? TargetId { get; set; }
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    [Column(TypeName="jsonb")]
    public string DetailsJson { get; set; } = "{}";
}
