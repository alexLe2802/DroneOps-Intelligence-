using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Waypoint")]
public sealed class Waypoint
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public int SequenceOrder { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public string? ActionType { get; set; }
    private static readonly System.Text.Json.JsonSerializerOptions PlanReadOptions = new() { PropertyNameCaseInsensitive = true };
    public string? NavigationJson { get; set; }
    [NotMapped]
    public WaypointNavigation? Navigation
    {
        get => NavigationJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<WaypointNavigation>(NavigationJson, PlanReadOptions);
        set => NavigationJson = value == null ? null : System.Text.Json.JsonSerializer.Serialize(value);
    }

    public DateTime CreatedAt { get; set; }

    public Mission? Mission { get; set; }
}
