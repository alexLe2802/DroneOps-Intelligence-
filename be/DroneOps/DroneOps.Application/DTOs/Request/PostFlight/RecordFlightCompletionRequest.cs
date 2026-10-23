namespace DroneOps.Application.DTOs.Request.PostFlight;

public sealed class RecordFlightCompletionRequest
{
    public Guid MissionId { get; set; }
    public Guid UavId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string EventType { get; set; } = "MISSION_COMPLETED";
    public DateTimeOffset CompletedAt { get; set; }
    public string? Evidence { get; set; }
}
