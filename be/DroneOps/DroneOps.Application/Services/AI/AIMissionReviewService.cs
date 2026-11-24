using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Response;
using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Application.Services;

public class AIMissionReviewService : IAIMissionReviewService
{
    private readonly IGenericRepository<Mission> _missionRepository;

    private readonly IGenericRepository<MissionUpdateRequest>
        _missionUpdateRequestRepository;

    private readonly IMissionValidationService _validationService;

    private readonly HttpClient _httpClient;

    private readonly string _apiKey;
    private readonly string _model;
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private static readonly HashSet<string> AllowedRiskLevels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "LOW",
            "MEDIUM",
            "HIGH"
        };

    public AIMissionReviewService(
    IGenericRepository<Mission> missionRepository,
    IGenericRepository<MissionUpdateRequest>
        missionUpdateRequestRepository,
    IMissionValidationService validationService,
    HttpClient httpClient,
    IConfiguration configuration)
    {
        _missionRepository = missionRepository;

        _missionUpdateRequestRepository =
            missionUpdateRequestRepository;

        _validationService = validationService;

        _httpClient = httpClient;

        _apiKey = configuration["Ai:ApiKey"]
            ?? throw new InvalidOperationException(
                "Gemini API key is not configured.");

        _model = configuration["Ai:Model"]
            ?? throw new InvalidOperationException(
                "Gemini model is not configured.");
    }
    public async Task<AIMissionReviewResponse> ReviewMissionAsync(
     Guid missionId,
     Guid actorId,
     CancellationToken cancellationToken = default)
    {
        // 1. Get mission by ID
        var mission = await _missionRepository.GetByIdAsync(
            missionId,
            cancellationToken);

        if (mission is null)
        {
            throw new KeyNotFoundException(
                $"Mission with ID '{missionId}' was not found.");
        }

        // 2. Only Pending Review missions can use AI review
        if (!string.Equals(
                mission.Status,
                "Pending Review",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AI review is only available for missions " +
                "with Pending Review status.");
        }

        // 3. Get the pending proposal for this mission
        var pendingRequest =
            await _missionUpdateRequestRepository.FirstOrDefaultAsync(
                x =>
                    x.MissionId == missionId &&
                    x.Status == "Pending",
                cancellationToken);

        if (pendingRequest is null)
        {
            throw new InvalidOperationException(
                "The mission does not have a pending review request.");
        }

        if (!pendingRequest.ValidationRunId.HasValue)
        {
            throw new InvalidOperationException(
                "The pending request does not contain validation evidence.");
        }

        // 4. Read the exact validation run stored during submission
        var evidence = await _validationService.ReadRunAsync(
            missionId,
            pendingRequest.ValidationRunId.Value,
            actorId,
            true,
            cancellationToken);

        if (evidence is null)
        {
            throw new InvalidOperationException(
                "Stored mission validation evidence was not found.");
        }

        var weather = evidence.Weather;

        // 5. Prepare stored weather evidence for Gemini
        object weatherData;

        if (weather is null)
        {
            weatherData = new
            {
                Available = false,
                Status = "Unavailable",
                Message =
                    "No stored weather evidence was found " +
                    "for this validation run."
            };
        }
        else
        {
            weatherData = new
            {
                weather.Available,
                weather.Status,
                weather.OverallRisk,
                weather.OverallRiskScore,

                weather.FlightStartTime,
                weather.FlightEndTime,

                weather.FetchedAt,
                weather.ExpiresAt,

                IsExpired =
                    weather.ExpiresAt <=
                    DateTimeOffset.UtcNow,

                weather.MaxWindMetersPerSecond,
                weather.MaxGustMetersPerSecond,
                weather.MaxCrosswindMetersPerSecond,
                weather.MaxRainMillimetersPerHour,

                weather.ThunderstormForecast,
                weather.Warnings,

                RouteWeather = weather.Waypoints.Select(x => new
                {
                    x.SequenceOrder,
                    x.IsRouteSample,
                    x.SampleOrder,
                    x.SegmentFrom,
                    x.SegmentTo,

                    x.Latitude,
                    x.Longitude,
                    x.AltitudeMeters,

                    x.EstimatedArrivalTime,
                    x.ForecastTime,

                    x.TemperatureCelsius,
                    x.RelativeHumidityPercent,

                    x.WindSpeedMetersPerSecond,
                    x.WindDirectionDegrees,
                    x.WindGustMetersPerSecond,
                    x.CrosswindMetersPerSecond,
                    x.HeadwindMetersPerSecond,

                    x.PrecipitationProbabilityPercent,
                    x.PrecipitationMillimeters,

                    x.VisibilityMeters,
                    x.CloudCoverPercent,
                    x.WeatherCode,

                    x.Status,
                    x.RiskLevel,
                    x.RiskScore,
                    x.RiskReasons,
                    x.Warnings
                }).ToList()
            };
        }

        // 6. Mission + saved validation/weather evidence
        var reviewData = new
        {
            Mission = new
            {
                mission.Id,
                mission.Name,
                mission.Description,
                mission.Status,
                mission.UAVId,

                mission.StartTime,
                mission.EndTime,

                mission.AltitudeReference,

                mission.AverageSpeedMetersPerSecond,
                mission.MaxSpeedMetersPerSecond,

                mission.AutomaticFlightRequested,
                mission.AutomaticFlightLimitsJson,

                mission.CreatedAt
            },

            Validation = new
            {
                ValidationRunId =
                    pendingRequest.ValidationRunId.Value,

                UpdateRequestId =
                    pendingRequest.Id,

                pendingRequest.IsInitial,
                pendingRequest.CreatedAt,

                Weather = weatherData
            }
        };

        var reviewJson = JsonSerializer.Serialize(
            reviewData,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        // 7. AI instructions
        var systemPrompt = """
        You are an AI advisory assistant supporting an
        Operation Manager in reviewing a UAV mission.

        The mission has already been submitted by a pilot
        and is currently in Pending Review status.

        IMPORTANT RULES:

        - The Operation Manager is the final decision maker.
        - You do NOT approve or reject the mission.
        - You only provide review advice.
        - Analyze only the supplied mission and validation data.
        - Do not invent missing information.
        - Do not infer pedestrian density, traffic, terrain,
          obstacles, permits or regulations from a location name.
        - The weather data is stored validation evidence captured
          when the pilot submitted the mission.
        - Do not claim that weather is safe when the evidence is
          unavailable, partial, stale or expired.
        - Treat expired weather evidence as a review concern.
        - Use the supplied weather risk, warnings and route weather.
        - Do not claim that geofence validation passed unless
          geofence evidence is explicitly supplied.
        - Do not claim that battery validation passed unless
          battery evidence is explicitly supplied.
        - Do not claim that the mission is safe to execute.

        Review:

        - Mission purpose and description
        - Mission schedule
        - Altitude reference
        - Average and maximum speed
        - Manual or automatic flight mode
        - Automatic flight limits
        - Weather evidence availability and expiration
        - Overall weather risk and score
        - Wind, gust and crosswind
        - Rain and precipitation probability
        - Visibility
        - Temperature
        - Thunderstorm forecast
        - Weather warnings along the route

        riskLevel must be exactly one of:

        LOW
        MEDIUM
        HIGH

        advice:
        Give a concise overall assessment.
        Maximum 2 sentences.

        risks:
        List only important risks supported by the supplied data.
        Keep every item short and clear.

        recommendation:
        Give one concise next-step recommendation to the
        Operation Manager.

        Do not repeat the same information across fields.
        Do not use Markdown.
        Return only JSON matching the required schema.
        """;

        // 8. Build Gemini request
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
                        text =
                            "Review the following mission and its " +
                            "stored weather validation evidence:\n" +
                            reviewJson
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
                        advice = new
                        {
                            type = "string"
                        },

                        riskLevel = new
                        {
                            type = "string"
                        },

                        risks = new
                        {
                            type = "array",

                            items = new
                            {
                                type = "string"
                            }
                        },

                        recommendation = new
                        {
                            type = "string"
                        }
                    },

                    required = new[]
                    {
                    "advice",
                    "riskLevel",
                    "risks",
                    "recommendation"
                }
                }
            }
        };

        // 9. Call Gemini
        var aiContent = await CallGeminiAsync(
            requestBody,
            cancellationToken);

        AIMissionReviewResponse? result;

        try
        {
            result = JsonSerializer.Deserialize<AIMissionReviewResponse>(
                aiContent,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Gemini returned malformed mission review JSON.",
                exception);
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid mission review response.");
        }

        ValidateResult(result);

        // Mission identity always comes from database
        result.MissionId = mission.Id;
        result.MissionName = mission.Name;

        return result;
    }

    private async Task<string> CallGeminiAsync(
        object requestBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(_model)}:generateContent");

        request.Headers.Add(
            "x-goog-api-key",
            _apiKey);

        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini review is unavailable. " +
                $"Provider status: {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(responseJson);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "candidates",
                out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Gemini response does not contain a review result.");
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
                "Gemini response does not contain review content.");
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty(
                    "text",
                    out var textElement))
            {
                continue;
            }

            var text = textElement.GetString();

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new InvalidOperationException(
            "Gemini returned empty review content.");
    }

    private static void ValidateResult(
        AIMissionReviewResponse result)
    {
        if (string.IsNullOrWhiteSpace(result.RiskLevel) ||
            !AllowedRiskLevels.Contains(result.RiskLevel))
        {
            throw new InvalidOperationException(
                "Gemini returned an invalid risk level.");
        }

        result.RiskLevel =
            result.RiskLevel.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(result.Advice))
        {
            throw new InvalidOperationException(
                "Gemini response is missing advice.");
        }

        result.Risks ??= [];

        if (string.IsNullOrWhiteSpace(result.Recommendation))
        {
            throw new InvalidOperationException(
                "Gemini response is missing recommendation.");
        }
    }
}
