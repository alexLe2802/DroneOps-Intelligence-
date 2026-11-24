using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("PilotLicenseAudit")]
public sealed class PilotLicenseAudit
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string PreviousStatus { get; set; } = string.Empty;

    public string NewStatus { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public Guid ActorId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Pilot Pilot { get; set; } = null!;
}
