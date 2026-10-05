using System.Reflection;

namespace DroneOps.Domain.Entities;

public class UAV
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string Status { get; set; } = "Available";

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Mission> Missions { get; set; }
        = new List<Mission>();

    public ICollection<TelemetryRecord> TelemetryRecords { get; set; }
        = new List<TelemetryRecord>();
}