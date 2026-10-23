using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("FlightCompletionEvent")]
public sealed class FlightCompletionEvent
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Guid UAVId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string EventType { get; set; } = "MISSION_COMPLETED";
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool IsTrusted { get; set; }
    public string? Evidence { get; set; }
    public Mission Mission { get; set; } = null!;
}
