using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.AI;

public interface IAIService
{
    Task<MissionSuggestionData> GenerateMissionSuggestionAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}