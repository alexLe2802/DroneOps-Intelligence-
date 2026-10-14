using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("MissionSuggestion")]
public sealed class MissionSuggestion
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string? MissionName { get; set; }

    public string? MissionPurpose { get; set; }

    public decimal? SuggestedAltitude { get; set; }

    public int? SuggestedWaypointCount { get; set; }

    public int? SuggestedFlightDuration { get; set; }

    public string? RiskAssessment { get; set; }

    public string? Recommendations { get; set; }

    public string AiResponse { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Pilot? Pilot { get; set; }
}