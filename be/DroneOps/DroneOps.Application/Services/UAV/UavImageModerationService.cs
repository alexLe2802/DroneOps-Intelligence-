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
    public async Task<bool> IsAllowedAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["UavImageModeration:ApiKey"];
        var model = configuration["UavImageModeration:Model"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("The UAV image moderation Gemini API key is not configured; upload was rejected.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogError("The UAV image moderation Gemini model is not configured; upload was rejected.");
            return false;
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
                logger.LogWarning("Gemini UAV image moderation returned status {StatusCode}; upload was rejected.", (int)response.StatusCode);
                return false;
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
                logger.LogWarning("Gemini UAV image moderation returned empty content; upload was rejected.");
                return false;
            }

            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            if (!root.TryGetProperty("isDrone", out var isDrone) ||
                !root.TryGetProperty("containsInappropriateContent", out var inappropriate) ||
                isDrone.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                inappropriate.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                logger.LogWarning("Gemini UAV image moderation returned an invalid result; upload was rejected.");
                return false;
            }

            return isDrone.GetBoolean() && !inappropriate.GetBoolean();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Gemini UAV image moderation failed; upload was rejected.");
            return false;
        }
    }
}
