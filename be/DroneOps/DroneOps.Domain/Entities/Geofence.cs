using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Geofence")]
public class Geofence
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Column(TypeName = "jsonb")]
    public string Coordinates { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
}
