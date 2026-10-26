using System.Net.Http.Headers;
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
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenAI API key is not configured; UAV image AI moderation was skipped.");
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model = configuration["OpenAI:VisionModel"] ?? "gpt-4o-mini",
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Classify the image for a UAV registry. Return JSON only with boolean fields isDrone and containsInappropriateContent. A drone includes UAVs, quadcopters, fixed-wing unmanned aircraft, and real devices viewed top-down or at an angle. Inappropriate content includes nudity, explicit sexual content, violence, or other unsafe imagery."
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Is this a genuine drone/UAV image and free of inappropriate content?" },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}", detail = "low" }
                            }
                        }
                    }
                },
                response_format = new { type = "json_object" },
                temperature = 0,
                max_tokens = 80
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI UAV image moderation returned status {StatusCode}; technical validation fallback was used.", (int)response.StatusCode);
                return true;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
            if (message.TryGetProperty("refusal", out var refusal) &&
                refusal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(refusal.GetString()))
            {
                return false;
            }
            var content = message.GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("OpenAI UAV image moderation returned empty content; technical validation fallback was used.");
                return true;
            }

            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            if (!root.TryGetProperty("isDrone", out var isDrone) ||
                !root.TryGetProperty("containsInappropriateContent", out var inappropriate) ||
                isDrone.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                inappropriate.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                logger.LogWarning("OpenAI UAV image moderation returned an invalid result; technical validation fallback was used.");
                return true;
            }

            return isDrone.GetBoolean() && !inappropriate.GetBoolean();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "OpenAI UAV image moderation failed; technical validation fallback was used.");
            return true;
        }
    }
}
