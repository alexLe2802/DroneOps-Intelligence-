using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;
[Table("MissionFlightExecution")]
public sealed class MissionFlightExecution
{
    [Key] public Guid MissionId { get; set; }
    public Guid UavId { get; set; }
    public Guid ConnectionId { get; set; }
    public string PlanHash { get; set; } = "";
    public string CompiledHash { get; set; } = "";
    public string ApprovedPlanJson { get; set; } = "{}";
    public string WaypointCommandsJson { get; set; } = "[]";
    public string ReachedCommandsJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Outcome { get; set; } = "InProgress";
}
[Table("MonitoringRuleRevision")]
public sealed class MonitoringRuleRevision
{
    [Key] public Guid Id { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public Guid ChangedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
