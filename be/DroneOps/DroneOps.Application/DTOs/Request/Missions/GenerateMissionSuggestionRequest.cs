namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class GenerateMissionSuggestionRequest
{
    public string Prompt { get; set; } = string.Empty;
}