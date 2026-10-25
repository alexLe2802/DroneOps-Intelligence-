namespace DroneOps.Application.DTOs.Response.Incidents;

public sealed class IncidentTransitionResponse
{
    public Guid Id { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IncidentAlertResponse
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid MissionId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool IsRead => ReadAt.HasValue;
}
