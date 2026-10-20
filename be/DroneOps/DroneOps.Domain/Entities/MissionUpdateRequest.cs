using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;

[Table("MissionUpdateRequest")]
public sealed class MissionUpdateRequest
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid ActorId { get; set; }
    public bool IsInitial { get; set; }
    public string Status { get; set; } = "Pending";
    public string BasePlanHash { get; set; } = "";
    [Column(TypeName = "jsonb")]
    public string BasePlanJson { get; set; } = "{}";
    [Column(TypeName = "jsonb")]
    public string ProposedPlanJson { get; set; } = "{}";
    public Guid? ValidationRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Reason { get; set; }
}

[Table("GeofenceRevision")]
public sealed class GeofenceRevision
{
    public Guid Id { get; set; }
    public Guid GeofenceId { get; set; }
    public long Revision { get; set; }
    public string Action { get; set; } = "Updated";
    [Column(TypeName = "jsonb")]
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset RecordedAt { get; set; }
}

[Table("MissionWeatherCache")]
public sealed class MissionWeatherCache
{
    public string Key { get; set; } = "";
    [Column(TypeName = "jsonb")]
    public string ForecastJson { get; set; } = "{}";
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
