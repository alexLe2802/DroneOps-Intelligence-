namespace DroneOps.Application.DTOs.Request.Incidents;

public sealed class AcknowledgeIncidentRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string ExpectedStatus { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class ResolveIncidentRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string ExpectedStatus { get; set; } = string.Empty;
    public string ResolutionNotes { get; set; } = string.Empty;
}
