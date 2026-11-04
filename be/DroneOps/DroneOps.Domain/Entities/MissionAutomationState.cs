using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("MissionAutomationState")]
public sealed class MissionAutomationState
{
    [Key] public Guid MissionId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset? LastPresenceAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedPlanHash { get; set; }
    public Guid? ConnectionId { get; set; }
}
