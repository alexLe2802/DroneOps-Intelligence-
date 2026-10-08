using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionSuggestionService
    : IMissionSuggestionService
{
    private readonly IAIService _aiService;

    private readonly IMissionSuggestionRepository
        _repository;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public MissionSuggestionService(
        IAIService aiService,
        IMissionSuggestionRepository repository)
    {
        _aiService = aiService;
        _repository = repository;
    }

    public async Task<MissionSuggestionResponse> GenerateAsync(
        Guid userId,
        GenerateMissionSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var pilotId =
            await GetPilotIdAsync(
                userId,
                cancellationToken);

        var prompt = request.Prompt.Trim();

        var aiData =
            await _aiService.GenerateMissionSuggestionAsync(
                prompt,
                cancellationToken);

        var rawAiResponse =
            JsonSerializer.Serialize(
                aiData,
                JsonOptions);

        var suggestion = new MissionSuggestion
        {
            Id = Guid.NewGuid(),

            PilotId = pilotId,

            Prompt = prompt,

            MissionName =
                Normalize(aiData.MissionName),

            MissionPurpose =
                Normalize(aiData.MissionPurpose),

            SuggestedAltitude =
                aiData.SuggestedAltitude,

            SuggestedWaypointCount =
                aiData.SuggestedWaypointCount,

            SuggestedFlightDuration =
                aiData.SuggestedFlightDuration,

            RiskAssessment =
                Normalize(aiData.RiskAssessment),

            Recommendations =
                Normalize(aiData.Recommendations),

            AiResponse = rawAiResponse,

            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(
            suggestion,
            cancellationToken);

        var saved =
            await _repository.SaveChangesAsync(
                cancellationToken);

        if (!saved)
        {
            throw new InvalidOperationException(
                "Could not save mission suggestion.");
        }

        return MapToResponse(
            suggestion,
            aiData);
    }

    public async Task<List<MissionSuggestionResponse>>
        GetMineAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var pilotId =
            await GetPilotIdAsync(
                userId,
                cancellationToken);

        var suggestions =
            await _repository.GetByPilotIdAsync(
                pilotId,
                cancellationToken);

        return suggestions
            .Select(MapToResponse)
            .ToList();
    }

    public async Task<MissionSuggestionResponse>
        GetMineByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        var pilotId =
            await GetPilotIdAsync(
                userId,
                cancellationToken);

        var suggestion =
            await _repository.GetOwnedByIdAsync(
                id,
                pilotId,
                cancellationToken);

        if (suggestion is null)
        {
            throw new KeyNotFoundException(
                "Mission suggestion was not found.");
        }

        return MapToResponse(suggestion);
    }

    private async Task<Guid> GetPilotIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var pilotId =
            await _repository.GetPilotIdByUserIdAsync(
                userId,
                cancellationToken);

        if (!pilotId.HasValue)
        {
            throw new KeyNotFoundException(
                "Pilot profile was not found.");
        }

        return pilotId.Value;
    }

    private static MissionSuggestionResponse MapToResponse(
        MissionSuggestion suggestion)
    {
        MissionSuggestionData? aiData = null;

        try
        {
            aiData =
                JsonSerializer.Deserialize<
                    MissionSuggestionData>(
                        suggestion.AiResponse,
                        JsonOptions);
        }
        catch (JsonException)
        {
            // Các cột chính vẫn được trả về.
        }

        return MapToResponse(
            suggestion,
            aiData);
    }

    private static MissionSuggestionResponse MapToResponse(
        MissionSuggestion suggestion,
        MissionSuggestionData? aiData)
    {
        return new MissionSuggestionResponse
        {
            Id = suggestion.Id,

            PilotId = suggestion.PilotId,

            Prompt = suggestion.Prompt,

            MissionName =
                suggestion.MissionName
                ?? string.Empty,

            MissionPurpose =
                suggestion.MissionPurpose
                ?? string.Empty,

            SuggestedAltitude =
                suggestion.SuggestedAltitude
                ?? 0,

            SuggestedWaypointCount =
                suggestion.SuggestedWaypointCount
                ?? 0,

            SuggestedFlightDuration =
                suggestion.SuggestedFlightDuration
                ?? 0,

            RiskAssessment =
                suggestion.RiskAssessment
                ?? string.Empty,

            Recommendations =
                suggestion.Recommendations
                ?? string.Empty,

            Summary =
                aiData?.Summary
                ?? string.Empty,

            CreatedAt = suggestion.CreatedAt
        };
    }

    private static string? Normalize(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}