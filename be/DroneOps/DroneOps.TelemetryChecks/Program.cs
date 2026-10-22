using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DroneOps.API.Controllers.Integrations;
using DroneOps.Application;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string integrationKey = "telemetry-check-key-not-for-production";
const string jwtKey = "telemetry-check-jwt-signing-key-at-least-32-bytes";
var pilotId = Guid.NewGuid();
var managerId = Guid.NewGuid();
var fixtureUavId = Guid.NewGuid();
var otherUavId = Guid.NewGuid();
var missionId = Guid.NewGuid();
var emptyMissionId = Guid.NewGuid();
var databaseName = "telemetry-e2e-" + Guid.NewGuid();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Error);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["IntegrationSettings:ApiKey"] = integrationKey,
    ["TelemetryMonitoring:LowBatteryPercent"] = "20",
    ["TelemetryMonitoring:MaximumAltitudeMeters"] = "120",
    ["TelemetryMonitoring:MaximumGroundSpeedMetersPerSecond"] = "25",
    ["TelemetryMonitoring:StaleAfterSeconds"] = "1",
    ["TelemetryMonitoring:OfflineAfterSeconds"] = "2"
});
builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName));
builder.Services.AddScoped<IMissionRepository, MissionRepository>();
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddControllers().AddApplicationPart(typeof(TelemetryIntegrationController).Assembly);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = "telemetry-checks",
        ValidateAudience = true,
        ValidAudience = "telemetry-check-client",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Users.Add(new User { Id = pilotId, FullName = "Fixture Pilot", Email = "pilot@fixture.test", PasswordHash = "fixture", CreatedAt = DateTimeOffset.UtcNow });
    db.UAVs.AddRange(
        new UAV { Id = fixtureUavId, PilotId = pilotId, Code = "SIM-001", Name = "PX4 fixture", Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
        new UAV { Id = otherUavId, PilotId = pilotId, Code = "SIM-002", Name = "Wrong mapping fixture", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    db.Missions.Add(new Mission { Id = missionId, CreatedBy = pilotId, UAVId = fixtureUavId, Name = "KAN-44 smoke mission", Status = "In Progress", CreatedAt = DateTime.UtcNow });
    db.Missions.Add(new Mission { Id = emptyMissionId, CreatedBy = pilotId, UAVId = fixtureUavId, Name = "Missing telemetry fixture", Status = "Approved", CreatedAt = DateTime.UtcNow });
    for (var i = 1; i <= 4; i++)
        db.Waypoints.Add(new Waypoint { Id = Guid.NewGuid(), MissionId = missionId, SequenceOrder = i, Latitude = 10.84m + i / 1000m, Longitude = 106.77m + i / 1000m, CreatedAt = DateTime.UtcNow });
    await db.SaveChangesAsync();
}

await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

HttpRequestMessage Ingest(string key, object payload, string? credential = integrationKey)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/telemetry/samples");
    request.Headers.Add("Idempotency-Key", key);
    if (credential != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    request.Content = JsonContent.Create(payload);
    return request;
}

object Sample(DateTimeOffset timestamp, Guid? mappedUav = null, decimal? latitude = 10.849m, decimal? longitude = 106.772m, int? battery = 74, int sequence = 2) => new
{
    sourceId = "sim-uav-001", uavId = mappedUav ?? fixtureUavId, missionId, sourceTimestamp = timestamp,
    latitude, longitude, altitudeMeters = (decimal?)82.4m, altitudeReference = "Relative",
    groundSpeedMetersPerSecond = (decimal?)12.6m, headingDegrees = (decimal?)215.5m,
    batteryPercent = battery, batteryVoltageVolts = (decimal?)15.8m,
    boardTemperatureCelsius = (decimal?)47.2m,
    flightMode = "AUTO", armed = true, missionSequence = sequence
};

string Token(Guid user, string role)
{
    var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256);
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        issuer: "telemetry-checks", audience: "telemetry-check-client",
        claims: [new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.Role, role)],
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials));
}

async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

try
{
    var now = DateTimeOffset.UtcNow;
    Check((await client.SendAsync(Ingest("auth-missing", Sample(now), null))).StatusCode == HttpStatusCode.Unauthorized, "invalid/missing integration credential is rejected");
    Check((await client.SendAsync(Ingest("invalid-range", Sample(now, latitude: 91m)))).StatusCode == HttpStatusCode.BadRequest, "invalid coordinate is rejected by API validation");
    Check((await client.SendAsync(Ingest("wrong-mapping", Sample(now, otherUavId)))).StatusCode == HttpStatusCode.BadRequest, "mission-to-UAV mapping mismatch is rejected");

    var missingGps = await client.SendAsync(Ingest("missing-gps", Sample(now.AddSeconds(-2), latitude: null, longitude: null)));
    Check(missingGps.StatusCode == HttpStatusCode.Created, $"missing GPS packet is accepted without inventing zero coordinates ({missingGps.StatusCode}: {await missingGps.Content.ReadAsStringAsync()})");

    var localOffsetTimestamp = now.AddSeconds(-3).ToOffset(TimeSpan.FromHours(7));
    var localOffset = await client.SendAsync(Ingest("utc-normalization", Sample(localOffsetTimestamp)));
    Check(localOffset.StatusCode == HttpStatusCode.Created,
        "local +07:00 timestamp is normalized to UTC before PostgreSQL persistence");

    var firstKey = "sim-uav-001:100:" + now.ToUnixTimeMilliseconds();
    var first = await client.SendAsync(Ingest(firstKey, Sample(now)));
    Check(first.StatusCode == HttpStatusCode.Created, "valid simulator sample reaches HTTP ingest and DB");
    Check(!(await Json(first)).GetProperty("duplicate").GetBoolean(), "first delivery is not marked duplicate");
    var duplicate = await client.SendAsync(Ingest(firstKey, Sample(now)));
    Check(duplicate.StatusCode == HttpStatusCode.OK && (await Json(duplicate)).GetProperty("duplicate").GetBoolean(), "retry is idempotent");

    var newestAt = now.AddSeconds(1);
    Check((await client.SendAsync(Ingest("newest", Sample(newestAt, sequence: 3)))).StatusCode == HttpStatusCode.Created, "newer packet is accepted");
    var late = await client.SendAsync(Ingest("late", Sample(now.AddSeconds(-1), sequence: 1)));
    var lateBody = await Json(late);
    Check(late.StatusCode == HttpStatusCode.Created && lateBody.GetProperty("lateArrival").GetBoolean() && !lateBody.GetProperty("usedForLatestProjection").GetBoolean(), "out-of-order packet is historical only");
    var lateReplay = await Json(await client.SendAsync(Ingest("late", Sample(now.AddSeconds(-1), sequence: 1))));
    Check(lateReplay.GetProperty("duplicate").GetBoolean() && lateReplay.GetProperty("lateArrival").GetBoolean() && !lateReplay.GetProperty("usedForLatestProjection").GetBoolean(), "replayed late packet never reports itself as the fresh projection");
    var skewed = await client.SendAsync(Ingest("future-clock-skew", Sample(now.AddMinutes(5), sequence: 99)));
    Check(skewed.StatusCode == HttpStatusCode.Created && !(await Json(skewed)).GetProperty("usedForLatestProjection").GetBoolean(), "future clock-skew packet is retained but excluded from latest");

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(managerId, "operation_manager"));
    var missing = await Json(await client.GetAsync($"/api/missions/{emptyMissionId}/telemetry/latest"));
    Check(missing.GetProperty("snapshotKind").GetString() == "Missing" && missing.GetProperty("sourceTimestamp").ValueKind == JsonValueKind.Null && missing.GetProperty("latitude").ValueKind == JsonValueKind.Null, "mission without samples returns the explicit Missing contract");
    var latestResponse = await client.GetAsync($"/api/missions/{missionId}/telemetry/latest");
    Check(latestResponse.StatusCode == HttpStatusCode.OK, $"manager can read latest API ({latestResponse.StatusCode})");
    var latest = await Json(latestResponse);
    Check(latest.GetProperty("missionSequence").GetInt32() == 3, "latest API keeps the newest projection");
    Check(latest.GetProperty("latitude").GetDecimal() == 10.849m && latest.GetProperty("progressPercent").GetDouble() == 75, "units, map coordinates and mission progress survive end-to-end");
    Check(latest.GetProperty("headingDegrees").GetDecimal() == 215.5m && latest.GetProperty("batteryVoltageVolts").GetDecimal() == 15.8m, "optional heading and battery voltage are preserved when supplied");
    Check(latest.GetProperty("environmentalTemperatureCelsius").ValueKind == JsonValueKind.Null && latest.GetProperty("relativeHumidityPercent").ValueKind == JsonValueKind.Null && latest.GetProperty("environmentalSensorStatus").GetString() == "Unavailable", "missing environmental sensors stay null/unavailable and are not weather substitutes");

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(await db.TelemetryRecords.CountAsync() == 6, "duplicate retry creates exactly one stored sample");
        Check((await db.TelemetryRecords.SingleAsync(x => x.IdempotencyKey == "utc-normalization")).SourceTimestamp.Offset == TimeSpan.Zero,
            "stored telemetry timestamp uses UTC offset");
        Check(await db.TelemetryRecords.AnyAsync(x => x.IdempotencyKey == "missing-gps" && x.Latitude == null && x.Longitude == null), "missing sensor values persist as null");
        var newest = await db.TelemetryRecords.SingleAsync(x => x.MissionSequence == 3);
        newest.ReceivedAt = DateTimeOffset.UtcNow.AddSeconds(-31);
        await db.SaveChangesAsync();
    }
    latest = await Json(await client.GetAsync($"/api/missions/{missionId}/telemetry/latest"));
    Check(latest.GetProperty("connectivityStatus").GetString() == "Offline" && latest.GetProperty("isStale").GetBoolean(), "packet loss exposes last-known data as stale/offline");
    await using (var scope = app.Services.CreateAsyncScope())
        Check((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Missions.FindAsync(missionId))!.Status == "In Progress", "packet loss never completes the mission");

    var reconnectAt = DateTimeOffset.UtcNow.AddSeconds(2);
    client.DefaultRequestHeaders.Authorization = null;
    Check((await client.SendAsync(Ingest("reconnect", Sample(reconnectAt, battery: 66, sequence: 4)))).StatusCode == HttpStatusCode.Created, "gateway reconnect packet is accepted");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(managerId, "operation_manager"));
    latest = await Json(await client.GetAsync($"/api/missions/{missionId}/telemetry/latest"));
    Check(latest.GetProperty("connectivityStatus").GetString() == "Online" && latest.GetProperty("batteryPercent").GetInt32() == 66, "reconnect restores a fresh snapshot");

    using var sseCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var sse = await client.GetAsync($"/api/live/stream?missionId={missionId}", HttpCompletionOption.ResponseHeadersRead, sseCts.Token);
    Check(sse.StatusCode == HttpStatusCode.OK && sse.Content.Headers.ContentType?.MediaType == "text/event-stream", "authenticated SSE stream opens");
    await using var stream = await sse.Content.ReadAsStreamAsync(sseCts.Token);
    using var reader = new StreamReader(stream);
    async Task<string> ReadEventAsync()
    {
        var lines = new List<string>();
        while (!sseCts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(sseCts.Token);
            if (line == null || (line.Length == 0 && lines.Count > 0)) break;
            if (line.Length > 0) lines.Add(line);
        }
        return string.Join('\n', lines);
    }
    var eventText = await ReadEventAsync();
    Check(eventText.Contains("event: telemetryUpdated") && eventText.Contains("\"batteryPercent\":66"), "SSE reconnect starts with the latest valid snapshot");
    var freshnessEvents = new List<string>();
    while (!freshnessEvents.Any(x => x.Contains("\"snapshotKind\":\"Offline\"")))
        freshnessEvents.Add(await ReadEventAsync());
    Check(freshnessEvents.Any(x => x.Contains("\"snapshotKind\":\"Stale\"")) && freshnessEvents.Any(x => x.Contains("\"snapshotKind\":\"Offline\"")), "idle SSE emits Online-to-Stale-to-Offline transitions without a new packet");

    Console.WriteLine($"\n{passed} telemetry end-to-end checks passed. Fixture/simulator only; no hardware verification claimed.");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}
