using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

internal static class DraftAnalysisChecks
{
    public static async Task RunAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = Guid.NewGuid();
        var pilot = new Pilot { Id = Guid.NewGuid(), UserId = actor };
        var uav = new UAV { Id = Guid.NewGuid(), Pilot = pilot, Status = "Active", MaxSpeedKmh = 36,
            MaxPayloadKg = 1, MaxWindSpeedMps = 3 };
        db.UAVs.Add(uav);
        await db.SaveChangesAsync();
        var providers = new Providers();
        var service = new DraftAnalysisService(db, providers, providers);
        var r = new DraftAnalysisRequest(uav.Id, "Survey", DateTimeOffset.UtcNow.AddDays(1), null,
            [new(1, 10, 106, 20, "TakeOff", null), new(2, 10.01m, 106, 20, "Land", null)],
            false, null, 12, null, 2, "AGL");
        try { await service.AnalyzeAsync(r with { Waypoints = [] }, actor, default); throw new Exception("Invalid route accepted"); }
        catch (ArgumentException) { }
        try { await service.AnalyzeAsync(r, Guid.NewGuid(), default); throw new Exception("Foreign aircraft accepted"); }
        catch (KeyNotFoundException) { }
        if (providers.Calls != 0) throw new Exception("Unexpected external call");
        var result = JsonSerializer.SerializeToElement(await service.AnalyzeAsync(r, actor, default));
        if (result.GetProperty("cruise").GetDecimal() != 10 || result.GetProperty("ceiling").GetDecimal() != 10 ||
            result.GetProperty("estimatedEnd").GetDateTimeOffset() <= r.StartTime ||
            result.GetProperty("windAlerts").GetArrayLength() != 1 ||
            !result.GetProperty("findings").EnumerateArray().Any(x => x.GetString()!.Contains("tải hoặc khối lượng")) ||
            providers.Calls != 2 || await db.Missions.AnyAsync()) throw new Exception("Draft analysis mismatch");
        var missing = JsonSerializer.SerializeToElement(await service.AnalyzeAsync(r with { Cruise = null, PayloadKg = null }, actor, default));
        if (missing.GetProperty("estimatedEnd").ValueKind != JsonValueKind.Null ||
            missing.GetProperty("cruise").ValueKind != JsonValueKind.Null) throw new Exception("Invented missing estimate");
        if (result.GetProperty("recommendedLimits").ValueKind != JsonValueKind.Null) throw new Exception("Manual draft returned automatic limits");
        var autoDraft = r with { Automatic = true, AltitudeReference = "Relative", Cruise = null, PayloadKg = 0.8m,
            Limits = AutomaticFlightPolicy.DefaultLimits() };
        var auto = JsonSerializer.SerializeToElement(await service.AnalyzeAsync(autoDraft, actor, default));
        var recommended = auto.GetProperty("recommendedLimits");
        if (recommended.ValueKind != JsonValueKind.Object) throw new Exception("Automatic draft missing recommended limits");
        var recLimits = JsonSerializer.Deserialize<AutomaticFlightLimits>(recommended.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new Exception("Unreadable recommended limits");
        foreach (var name in new[] { "TakeoffSpeedMetersPerSecond", "ClimbSpeedMetersPerSecond", "DescentSpeedMetersPerSecond",
            "LandingSpeedMetersPerSecond", "MaxAltitudeMeters", "MaxWindMetersPerSecond", "MaxGustMetersPerSecond" })
            if (!recommended.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"Recommended limit {name} missing");
        var autoPolicyErrors = AutomaticFlightPolicy.Errors(true, "Relative", 1m, 5m, recLimits,
            [(20m, "TakeOff"), (20m, "Land")]);
        if (autoPolicyErrors.Count != 0 || recLimits.ClimbSpeedMetersPerSecond != 4.9m || recLimits.MaxWindMetersPerSecond != 3m ||
            recLimits.PolicyVersion != "multicopter-envelope-v2")
            throw new Exception("Recommended limits violate automatic policy: " + string.Join("; ", autoPolicyErrors));
        Console.WriteLine("PASS: gated draft analysis, ownership, speed ceiling, route timing, payload, wind and missing inputs");
    }

    private sealed class Providers : IMissionWeatherQueryService, IAIService
    {
        public int Calls;
        public Task<WeatherAdvisory> ReadDraftAsync(Mission draft, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new WeatherAdvisory { Status = "Partial",
                Waypoints = [new WaypointWeather { SequenceOrder = 2, WindSpeedMetersPerSecond = 5, Status = "Partial" }] });
        }
        public Task<string> ExplainDraftAsync(string evidence, CancellationToken ct)
        { Calls++; return Task.FromResult("Kiểm tra tải trước khi bay."); }
        public Task<WeatherAdvisory> ReadAsync(Guid id, Guid actor, bool manager, bool refresh, CancellationToken ct, Guid? updateRequestId = null) =>
            throw new NotSupportedException();
        public Task<MissionSuggestionData> GenerateMissionSuggestionAsync(string prompt, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
