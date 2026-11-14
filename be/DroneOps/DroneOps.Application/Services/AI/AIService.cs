using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.AI;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Persistence.Services.AI;

public sealed class GeminiAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public GeminiAIService(
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
        var provider = _configuration["Ai:Provider"];
        var apiKey = _configuration["Ai:ApiKey"];
        var model = _configuration["Ai:Model"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "Gemini model is not configured.");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException(
                "Mission suggestion prompt is required.",
                nameof(prompt));
        }

        var endpoint =
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            endpoint);

        request.Headers.Add(
            "x-goog-api-key",
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

            Return data matching the required JSON schema.
            """;

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new object[]
                {
                    new
                    {
                        text = systemPrompt
                    }
                }
            },

            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            text = prompt
                        }
                    }
                }
            },

            generationConfig = new
            {
                temperature = 0.2,

                responseMimeType = "application/json",

                responseJsonSchema = new
                {
                    type = "object",

                    properties = new
                    {
                        missionName = new
                        {
                            type = "string"
                        },

                        missionPurpose = new
                        {
                            type = "string"
                        },

                        suggestedAltitude = new
                        {
                            type = "number",
                            minimum = 0
                        },

                        suggestedWaypointCount = new
                        {
                            type = "integer",
                            minimum = 0
                        },

                        suggestedFlightDuration = new
                        {
                            type = "number",
                            minimum = 0
                        },

                        riskAssessment = new
                        {
                            type = "string"
                        },

                        recommendations = new
                        {
                            type = "string"
                        },

                        summary = new
                        {
                            type = "string"
                        }
                    },

                    required = new[]
                    {
                        "missionName",
                        "missionPurpose",
                        "suggestedAltitude",
                        "suggestedWaypointCount",
                        "suggestedFlightDuration",
                        "riskAssessment",
                        "recommendations",
                        "summary"
                    }
                }
            }
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
                $"Gemini assessment is unavailable. " +
                $"Provider status: {(int)response.StatusCode}. " +
                $"Provider response: {responseBody}");
        }

        var aiContent =
            ExtractGeminiContent(responseBody);

        var result =
            JsonSerializer.Deserialize<MissionSuggestionData>(
                aiContent,
                JsonOptions);

        if (result is null)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid response.");
        }

        ValidateResult(result);

        return result;
    }

    private static string ExtractGeminiContent(
        string responseBody)
    {
        using var document =
            JsonDocument.Parse(responseBody);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "candidates",
                out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            var blockReason = TryGetBlockReason(root);

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(blockReason)
                    ? "Gemini response does not contain a result."
                    : $"Gemini blocked the request: {blockReason}");
        }

        var firstCandidate = candidates[0];

        if (!firstCandidate.TryGetProperty(
                "content",
                out var content) ||
            !content.TryGetProperty(
                "parts",
                out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Gemini response does not contain content.");
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty(
                    "text",
                    out var textElement))
            {
                var text = textElement.GetString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        throw new InvalidOperationException(
            "Gemini returned empty content.");
    }

    private static string? TryGetBlockReason(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "promptFeedback",
                out var promptFeedback))
        {
            return null;
        }

        if (!promptFeedback.TryGetProperty(
                "blockReason",
                out var blockReason))
        {
            return null;
        }

        return blockReason.GetString();
    }

    private static void ValidateResult(
        MissionSuggestionData result)
    {
        if (string.IsNullOrWhiteSpace(
                result.MissionName))
        {
            throw new InvalidOperationException(
                "Gemini response is missing mission name.");
        }

        if (result.SuggestedAltitude < 0)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid altitude.");
        }

        if (result.SuggestedWaypointCount < 0)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid waypoint count.");
        }

        if (result.SuggestedFlightDuration < 0)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid flight duration.");
        }
    }
}