namespace DroneOps.Application.DTOs.Request.Incidents;

public sealed class AcknowledgeIncidentRequest
{
    public string? Notes { get; set; }
}

public sealed class ResolveIncidentRequest
{
    public string ResolutionNotes { get; set; } = string.Empty;
}
