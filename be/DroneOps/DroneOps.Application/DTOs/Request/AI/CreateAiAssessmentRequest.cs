namespace DroneOps.Application.DTOs.Request.AI;

public sealed class CreateAiAssessmentRequest
{
    public string MissionRef { get; set; } = string.Empty;
    public int MissionVersion { get; set; } = 1;
}
