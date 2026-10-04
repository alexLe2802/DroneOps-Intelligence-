namespace DroneOps.Domain.Entities;

public sealed class Mission
{
    public Guid Id { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? UAVId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = "Draft";

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? Creator { get; set; }

    public UAV? UAV { get; set; }

    public ICollection<MissionVersion> MissionVersions { get; set; }
        = new List<MissionVersion>();
}