using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.AI;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Persistence.Services.AI;

public sealed class OpenAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public OpenAIService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<MissionSuggestionData>
        GenerateMissionSuggestionAsync(
            string prompt,
            CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];

        var model = _configuration["OpenAI:Model"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "OpenAI model is not configured.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "chat/completions");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                apiKey);

        var systemPrompt = """
            You are an AI advisory assistant for UAV mission planning
            inside a drone laboratory.

            The pilot is the final decision maker.
            Do not create, approve, validate or execute the mission.

            Analyze the pilot request and recommend:
            - mission name
            - mission purpose
            - altitude in meters
            - number of waypoints
            - flight duration in minutes
            - potential risks
            - safety recommendations
            - concise summary

            Do not claim that geofence validation passed.
            Formal validation is performed separately by the system.

            Return only valid JSON using exactly these keys:

            {
              "missionName": "string",
              "missionPurpose": "string",
              "suggestedAltitude": 0,
              "suggestedWaypointCount": 0,
              "suggestedFlightDuration": 0,
              "riskAssessment": "string",
              "recommendations": "string",
              "summary": "string"
            }
            """;

        var requestBody = new
        {
            model,

            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },

            response_format = new
            {
                type = "json_object"
            },

            temperature = 0.2
        };

        request.Content = JsonContent.Create(requestBody);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AI assessment is unavailable. " +
                $"Provider status: {(int)response.StatusCode}.");
        }

        var aiContent =
            ExtractAssistantContent(responseBody);

        var result =
            JsonSerializer.Deserialize<MissionSuggestionData>(
                aiContent,
                JsonOptions);

        if (result is null)
        {
            throw new InvalidOperationException(
                "AI returned an invalid response.");
        }

        ValidateResult(result);

        return result;
    }

    private static string ExtractAssistantContent(
        string responseBody)
    {
        using var document =
            JsonDocument.Parse(responseBody);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "choices",
                out var choices) ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "AI response does not contain a result.");
        }

        var content =
            choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "AI returned empty content.");
        }

        return content;
    }

    private static void ValidateResult(
        MissionSuggestionData result)
    {
        if (string.IsNullOrWhiteSpace(
                result.MissionName))
        {
            throw new InvalidOperationException(
                "AI response is missing mission name.");
        }

        if (result.SuggestedAltitude < 0)
        {
            throw new InvalidOperationException(
                "AI returned an invalid altitude.");
        }

        if (result.SuggestedWaypointCount < 0)
        {
            throw new InvalidOperationException(
                "AI returned an invalid waypoint count.");
        }

        if (result.SuggestedFlightDuration < 0)
        {
            throw new InvalidOperationException(
                "AI returned an invalid flight duration.");
        }
    }
}