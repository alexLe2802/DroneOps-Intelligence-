using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Mission")]
public sealed class Mission
{
    public Guid Id { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? UAVId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = "Draft";

    public string? AltitudeReference { get; set; }
    public bool AutomaticFlightRequested { get; set; }
    private static readonly System.Text.Json.JsonSerializerOptions PlanReadOptions = new() { PropertyNameCaseInsensitive = true };
    public string? AutomaticFlightLimitsJson { get; set; }
    [NotMapped]
    public AutomaticFlightLimits? AutomaticFlightLimits
    {
        get => AutomaticFlightLimitsJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<AutomaticFlightLimits>(AutomaticFlightLimitsJson, PlanReadOptions);
        set => AutomaticFlightLimitsJson = value == null ? null : System.Text.Json.JsonSerializer.Serialize(value);
    }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    
    public decimal? AverageSpeedMetersPerSecond { get; set; }

    
    public decimal? MaxSpeedMetersPerSecond { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public User? Creator { get; set; }

    public UAV? UAV { get; set; }

    public ICollection<Waypoint> Waypoints { get; set; } =
        new List<Waypoint>();
}
