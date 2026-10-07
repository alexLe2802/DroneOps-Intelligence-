using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DroneOps.API.AI;

public interface IAiAssessmentClient
{
    Task<AiAssessmentResult> AssessAsync(CreateAiAssessmentRequest request, CancellationToken ct);
}

public sealed class AiProviderException(string code, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
}

public sealed class GeminiAssessmentClient(HttpClient httpClient, IOptions<AiSettings> options) : IAiAssessmentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AiSettings settings = options.Value;

    public async Task<AiAssessmentResult> AssessAsync(CreateAiAssessmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new AiProviderException("provider_not_configured");
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions");
        message.Headers.Add("x-goog-api-key", settings.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = settings.Model,
            input = $"{SystemInstruction}\n\n{BuildPrompt(request)}",
            response_format = new
            {
                type = "text",
                mime_type = "application/json",
                schema = ResponseSchema
            }
        });

        HttpResponseMessage response;
        try { response = await httpClient.SendAsync(message, ct); }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested) { throw new AiProviderException("provider_timeout", e); }
        catch (HttpRequestException e) { throw new AiProviderException("provider_unavailable", e); }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new AiProviderException("provider_rate_limited");
            if (!response.IsSuccessStatusCode) throw new AiProviderException("provider_error");
            using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var text = ReadText(payload.RootElement);
            try
            {
                var result = JsonSerializer.Deserialize<AiAssessmentResult>(text, JsonOptions);
                return result is not null && !string.IsNullOrWhiteSpace(result.Summary)
                    ? result : throw new AiProviderException("invalid_provider_response");
            }
            catch (JsonException e) { throw new AiProviderException("invalid_provider_response", e); }
        }
    }

    private static string ReadText(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps)) throw new AiProviderException("invalid_provider_response");
        foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("type", out var type) || type.GetString() != "model_output" ||
                !step.TryGetProperty("content", out var content)) continue;
            foreach (var item in content.EnumerateArray())
                if (item.TryGetProperty("type", out var itemType) && itemType.GetString() == "text" &&
                    item.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } value) return value;
        }
        throw new AiProviderException("invalid_provider_response");
    }

    private static string BuildPrompt(CreateAiAssessmentRequest request) =>
        "Assess this UAV mission context. Use only the supplied facts; list missing data instead of inventing it.\n" +
        JsonSerializer.Serialize(request, JsonOptions);

    private const string SystemInstruction = """
        You are an advisory UAV mission risk analyst. Return a concise operational assessment.
        Never approve or reject a mission, never issue flight-control commands, and never claim regulatory certification.
        Base every risk on supplied context. Explicitly identify missing data. Precautions require human review.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["summary"] = new { type = "string" },
            ["risks"] = new { type = "array", items = new { type = "object", properties = new Dictionary<string, object>
                { ["severity"] = new { type = "string", @enum = new[] { "Low", "Moderate", "High", "Critical" } },
                  ["title"] = new { type = "string" }, ["evidence"] = new { type = "string" },
                  ["precaution"] = new { type = "string" } },
                required = new[] { "severity", "title", "evidence", "precaution" } } },
            ["precautions"] = new { type = "array", items = new { type = "string" } },
            ["missingData"] = new { type = "array", items = new { type = "string" } },
            ["disclaimer"] = new { type = "string" }
        },
        required = new[] { "summary", "risks", "precautions", "missingData", "disclaimer" }
    };
}
