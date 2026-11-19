using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Response;
using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Application.Services;

public class AIMissionReviewService : IAIMissionReviewService
{
    private readonly IGenericRepository<Mission> _missionRepository;
    private readonly HttpClient _httpClient;

    private readonly string _apiKey;
    private readonly string _model;

    public AIMissionReviewService(
        IGenericRepository<Mission> missionRepository,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _missionRepository = missionRepository;
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
        CancellationToken cancellationToken = default)
    {
        // 1. Lấy mission theo ID
        var mission = await _missionRepository.GetByIdAsync(
            missionId,
            cancellationToken);

        if (mission == null)
        {
            throw new KeyNotFoundException(
                $"Mission with ID '{missionId}' was not found.");
        }

        // 2. Chỉ review mission đang PendingReview
        if (!string.Equals(
          mission.Status,
          "Pending Review",
          StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AI review is only available for missions with Pending Review status.");
        }

        // 3. Chỉ lấy những thông tin AI thực sự cần
        var missionData = new
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
        };

        var missionJson = JsonSerializer.Serialize(
            missionData,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        // 4. Prompt
        var prompt = $"""
        You are an AI assistant in a UAV mission management system.

        Your job is to review a mission that has been submitted
        by a pilot and is currently waiting for manager review.

        IMPORTANT:
        - You only provide advice.
        - You do NOT approve or reject the mission.
        - The Operation Manager makes the final decision.
        - Analyze only the information provided.
        - Do not invent missing information.
        - Point out missing or suspicious mission information.
        - Consider mission schedule, flight speed, altitude reference,
          automatic flight request and automatic flight limits when available.

        Mission data:

        {missionJson}

        Give the manager a short and useful review.

        Use this format:

        Risk Level: LOW / MEDIUM / HIGH

        Advice:
        <your assessment>

        Risks:
        - <risk 1>
        - <risk 2>

        Recommendation:
        <what the manager should check before making the final decision>
        """;

        // 5. Gọi Gemini
        var advice = await CallGeminiAsync(
            prompt,
            cancellationToken);

        // 6. Trả kết quả
        return new AIMissionReviewResponse
        {
            MissionId = mission.Id,
            MissionName = mission.Name,
            Advice = advice
        };
    }

    private async Task<string> CallGeminiAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = prompt
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        using var response = await _httpClient.PostAsync(
            url,
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini review failed. " +
                $"Status: {(int)response.StatusCode}. " +
                $"Response: {responseJson}");
        }

        using var document = JsonDocument.Parse(responseJson);

        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Gemini did not return any review.");
        }

        var advice = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(advice))
        {
            throw new InvalidOperationException(
                "Gemini returned an empty review.");
        }

        return advice.Trim();
    }
}
