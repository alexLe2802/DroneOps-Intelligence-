using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.Interfaces.UAVs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

public sealed class UavImageModerationService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<UavImageModerationService> logger) : IUavImageModerationService
{
    public async Task<UavImageModerationResult> ModerateAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["UavImageModeration:ApiKey"];
        var model = configuration["UavImageModeration:Model"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("The UAV image moderation Gemini API key is not configured; moderation was deferred.");
            return new(UavImageModerationDecision.Unavailable, "Gemini is not configured.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogError("The UAV image moderation Gemini model is not configured; moderation was deferred.");
            return new(UavImageModerationDecision.Unavailable, "Gemini model is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"models/{Uri.EscapeDataString(model)}:generateContent");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = "Classify this image for a UAV registry. A drone includes UAVs, quadcopters, fixed-wing unmanned aircraft, and real devices viewed from any angle. Inappropriate content includes nudity, explicit sexual content, graphic violence, hate imagery, or other unsafe content. Return JSON only: {\"isDrone\":true|false,\"containsInappropriateContent\":true|false}." },
                            new { inline_data = new
                            {
                                mime_type = contentType,
                                data = Convert.ToBase64String(imageBytes)
                            } }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 256,
                    responseMimeType = "application/json",
                    thinkingConfig = new
                    {
                        thinkingLevel = "low"
                    }
                }
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini UAV image moderation returned status {StatusCode}; moderation was deferred.", (int)response.StatusCode);
                return new(UavImageModerationDecision.Unavailable, $"Gemini returned HTTP {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var content = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("Gemini UAV image moderation returned empty content; moderation was deferred.");
                return new(UavImageModerationDecision.Unavailable, "Gemini returned an empty response.");
            }

            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            if (!root.TryGetProperty("isDrone", out var isDrone) ||
                !root.TryGetProperty("containsInappropriateContent", out var inappropriate) ||
                isDrone.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                inappropriate.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                logger.LogWarning("Gemini UAV image moderation returned an invalid result; moderation was deferred.");
                return new(UavImageModerationDecision.Unavailable, "Gemini returned an invalid response.");
            }

            return isDrone.GetBoolean() && !inappropriate.GetBoolean()
                ? new(UavImageModerationDecision.Approved, "Gemini confirmed a safe UAV image.")
                : new(UavImageModerationDecision.Rejected, "The image is not a safe UAV image.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Gemini UAV image moderation failed; moderation was deferred.");
            return new(UavImageModerationDecision.Unavailable, "Gemini request failed temporarily.");
        }
    }
}
