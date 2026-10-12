using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionSuggestionService
{
    Task<MissionSuggestionResponse> GenerateAsync(
        Guid userId,
        GenerateMissionSuggestionRequest request,
        CancellationToken cancellationToken = default);

    Task<List<MissionSuggestionResponse>> GetMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MissionSuggestionResponse> GetMineByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}