using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("UAV")]
public class UAV
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTimeOffset CreatedAt { get; set; }

    public Pilot Pilot { get; set; } = null!;
}