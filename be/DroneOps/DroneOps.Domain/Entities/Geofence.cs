using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Geofence")]
public class Geofence
{
    public string RestrictionType { get; set; } = "Restricted";
    public string VerticalRule { get; set; } = "Unspecified";
    public string? AltitudeReference { get; set; }
    public decimal? MinAltitudeMeters { get; set; }
    public decimal? MaxAltitudeMeters { get; set; }
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long Revision { get; set; }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Column(TypeName = "jsonb")]
    public string Coordinates { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
}
