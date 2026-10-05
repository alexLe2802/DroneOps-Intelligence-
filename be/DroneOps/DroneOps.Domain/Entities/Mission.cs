namespace DroneOps.Domain.Entities;

public class Mission
{
    public Guid Id { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? UAVId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = "Draft";

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public UAV? UAV { get; set; }

    public ICollection<MissionVersion> Versions { get; set; }
        = new List<MissionVersion>();

    public ICollection<Incident> Incidents { get; set; }
        = new List<Incident>();

    public ICollection<MissionApproval> Approvals { get; set; }
        = new List<MissionApproval>();

    public ICollection<TelemetryRecord> TelemetryRecords { get; set; }
        = new List<TelemetryRecord>();
}