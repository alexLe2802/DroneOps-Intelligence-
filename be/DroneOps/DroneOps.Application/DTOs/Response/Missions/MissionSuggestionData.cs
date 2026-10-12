namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionSuggestionData
{
    public string MissionName { get; set; } = string.Empty;

    public string MissionPurpose { get; set; } = string.Empty;

    public decimal SuggestedAltitude { get; set; }

    public int SuggestedWaypointCount { get; set; }

    public int SuggestedFlightDuration { get; set; }

    public string RiskAssessment { get; set; } = string.Empty;

    public string Recommendations { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}