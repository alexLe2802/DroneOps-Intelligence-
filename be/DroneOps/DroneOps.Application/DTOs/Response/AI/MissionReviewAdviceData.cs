namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionReviewAdviceData
{
    public Guid MissionId { get; set; }

    public string MissionName { get; set; } = string.Empty;

    public string RiskLevel { get; set; } = string.Empty;

    public string Advice { get; set; } = string.Empty;

    public List<string> Risks { get; set; } = [];

    public string Recommendation { get; set; } = string.Empty;
}
