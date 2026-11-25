using System.Net;
using System.Text.Json;
using DroneOps.Persistence.Services.AI;
using Microsoft.Extensions.Configuration;

internal static class GeminiFallbackChecks
{
    public static async Task RunAsync()
    {
        foreach (var status in new[] { 401, 403, 404, 429, 500, 503 })
        {
            var handler = new ProviderHandler(status);
            var result = await Create(handler).GenerateMissionSuggestionAsync("Khao sat");
            Check(result.MissionName == "Khảo sát" && handler.Calls.SequenceEqual(new[]
                { ("gemini-4-carbon", "primary-test"), ("gemini-3.8-flash", "fallback-test") }),
                $"HTTP {status} switches both key and model");
        }

        var primary = new ProviderHandler(200);
        await Create(primary).GenerateMissionSuggestionAsync("Test mission");
        Check(primary.Calls.Count == 1, "successful Carbon requests do not spend fallback quota");

        var network = new ProviderHandler(0);
        await Create(network).GenerateMissionSuggestionAsync("Test mission");
        Check(network.Calls.Count == 2, "transport failure uses fallback");

        var invalid = new ProviderHandler(400);
        try { await Create(invalid).GenerateMissionSuggestionAsync("Test mission"); throw new Exception("Expected error"); }
        catch (InvalidOperationException error)
        {
            Check(invalid.Calls.Count == 1 && !error.Message.Contains("sensitive-provider-body"),
                "invalid requests are not retried and provider bodies are not exposed");
        }

        var legacy = new ProviderHandler(503);
        try { await Create(legacy, false).GenerateMissionSuggestionAsync("Test mission"); throw new Exception("Expected error"); }
        catch (InvalidOperationException) { Check(legacy.Calls.Count == 1, "single-key configuration remains supported"); }

        var exhausted = new ProviderHandler(429, 429);
        try { await Create(exhausted).GenerateMissionSuggestionAsync("Test mission"); throw new Exception("Expected error"); }
        catch (InvalidOperationException) { Check(exhausted.Calls.Count == 2, "exhausted keys stop after one attempt each"); }
    }

    private static GeminiAIService Create(ProviderHandler handler, bool fallback = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "Gemini", ["Ai:ApiKey"] = "primary-test", ["Ai:Model"] = "gemini-4-carbon"
        };
        if (fallback)
        {
            values["Ai:Fallback:ApiKey"] = "fallback-test";
            values["Ai:Fallback:Model"] = "gemini-3.8-flash";
        }
        return new GeminiAIService(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") },
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class ProviderHandler(int firstStatus, int nextStatus = 200) : HttpMessageHandler
    {
        public List<(string Model, string Key)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var instruction = payload.RootElement.GetProperty("system_instruction").GetProperty("parts")[0]
                .GetProperty("text").GetString();
            Check(instruction?.Contains("tiếng Việt có dấu") == true, "request specifies accented Vietnamese");
            Check(instruction?.Contains("Address only the pilot") == true &&
                instruction.Contains("Omit reviewer briefings"), "draft advice is scoped to the pilot");
            var model = request.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            Calls.Add((model, request.Headers.GetValues("x-goog-api-key").Single()));
            var status = Calls.Count == 1 ? firstStatus : nextStatus;
            if (status == 0) throw new HttpRequestException("Transport unavailable");
            const string result = """
                {"missionName":"Khảo sát","missionPurpose":"Test","suggestedAltitude":10,"suggestedWaypointCount":1,
                 "suggestedFlightDuration":1,"averageSpeedMetersPerSecond":1,"maxSpeedMetersPerSecond":2,
                 "altitudeReference":"AGL","automaticFlightRequested":false,
                 "waypoints":[{"sequenceOrder":1,"latitude":10,"longitude":106,"altitude":10,"actionType":"HOLD",
                 "navigation":{"speedMetersPerSecond":1,"holdTimeSeconds":0,"acceptanceRadiusMeters":1,
                 "passRadiusMeters":0,"yawDegrees":0,"emergencyLandingCandidate":false}}],
                 "riskAssessment":"Review","recommendations":"Review","summary":"Test"}
                """;
            var body = status == 200
                ? JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = result } } } } } })
                : "sensitive-provider-body";
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        }
    }
}
