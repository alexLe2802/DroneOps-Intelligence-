using System.Diagnostics;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Persistence.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DroneOps.API.Controllers.Integrations;
using DroneOps.API;
using DroneOps.Application;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Persistence.Repositories;
using DroneOps.Persistence.Interfaces.Notifications;
using DroneOps.Persistence.Repositories.Notifications;
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
    ["IntegrationSettings:Sources:0:SourceId"] = "sim-uav-001",
    ["IntegrationSettings:Sources:0:UavId"] = fixtureUavId.ToString(),
    ["IntegrationSettings:Sources:1:SourceId"] = "fence-source",
    ["IntegrationSettings:Sources:1:UavId"] = fixtureUavId.ToString(),
    ["TelemetryMonitoring:LowBatteryPercent"] = "20",
    ["TelemetryMonitoring:StaleAfterSeconds"] = "1",
    ["TelemetryMonitoring:OfflineAfterSeconds"] = "2"
});
builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName));
builder.Services.AddScoped<IMissionRepository, MissionRepository>();
builder.Services.AddScoped<IGeofenceRepository, GeofenceRepository>();
builder.Services.AddScoped<IPostFlightRepository, PostFlightRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IMissionWorkflowRepository, MissionWorkflowRepository>();
builder.Services.AddScoped<IMissionValidationRepository, MissionValidationRepository>();
builder.Services.AddScoped<IMissionApprovalRepository, MissionApprovalRepository>();
builder.Services.AddScoped<IMissionWeatherQueryRepository, MissionWeatherQueryRepository>();
builder.Services.AddScoped<IMissionPreflightRepository, MissionPreflightRepository>();
builder.Services.AddSingleton<MissionStreamSnapshots>();
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddSingleton<IDroneConnectionRepository,FixtureDroneConnectionRepository>();
builder.Services.AddRateLimiter(o=>{o.AddFixedWindowLimiter("gateway-pairing",p=>{p.PermitLimit=1000;p.Window=TimeSpan.FromMinutes(1);});o.AddFixedWindowLimiter("gateway-ingest",p=>{p.PermitLimit=1000;p.Window=TimeSpan.FromMinutes(1);});o.AddFixedWindowLimiter("gateway-websocket",p=>{p.PermitLimit=1000;p.Window=TimeSpan.FromMinutes(1);});});
builder.Services.AddControllers(o=>o.Filters.Add<MissionValidationExceptionFilter>()).AddApplicationPart(typeof(TelemetryIntegrationController).Assembly);
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
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Map("/ws/drone-gateway", DroneGatewayWebSocket.HandleAsync).RequireRateLimiting("gateway-websocket");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Users.Add(new User { Id = pilotId, FullName = "Fixture Pilot", Email = "pilot@fixture.test", PasswordHash = "fixture", CreatedAt = DateTimeOffset.UtcNow });
    db.UAVs.AddRange(
        new UAV { Id = fixtureUavId, PilotId = pilotId, Code = "SIM-001", Name = "Single drone fixture", MaxSpeedKmh = 90, Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
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

var idlePoll = new IdlePollDelay(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
Check(idlePoll.Next(false) == TimeSpan.FromSeconds(10), "empty worker queue starts backing off");
for (var cycle = 0; cycle < 12; cycle++) idlePoll.Next(false);
Check(idlePoll.Next(false) == TimeSpan.FromSeconds(60), "idle worker delay caps at sixty seconds");
Check(idlePoll.Next(true) == TimeSpan.FromSeconds(5), "productive worker immediately restores normal interval");
var sharedSnapshots = app.Services.GetRequiredService<MissionStreamSnapshots>();
MissionStreamSnapshots.Snapshot previousSnapshot;
using (var first = sharedSnapshots.Subscribe(missionId))
using (var second = sharedSnapshots.Subscribe(missionId))
{
    var results = await Task.WhenAll(first.ReadAsync(default), second.ReadAsync(default));
    Check(ReferenceEquals(results[0], results[1]), "concurrent SSE subscribers share one mission refresh");
    previousSnapshot = results[0];
    using var differentMission = sharedSnapshots.Subscribe(Guid.NewGuid());
    Check(!ReferenceEquals(previousSnapshot, await differentMission.ReadAsync(default)), "SSE snapshots are isolated by mission");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try { await second.ReadAsync(canceled.Token); Check(false, "canceled SSE read must stop"); }
    catch (OperationCanceledException) { Check(true, "canceled subscriber does not cancel other subscriptions"); }
    Check(ReferenceEquals(previousSnapshot, await first.ReadAsync(default)), "remaining SSE subscriber survives another reader cancellation");
}
using (var reconnected = sharedSnapshots.Subscribe(missionId))
    Check(!ReferenceEquals(previousSnapshot, await reconnected.ReadAsync(default)), "last SSE disconnect releases cached mission state");

var scheduled = DateTimeOffset.UtcNow.AddMinutes(10);
Check(!MissionAutomationService.InConfirmationWindow(scheduled.AddMinutes(-5).AddTicks(-1), scheduled, scheduled.AddMinutes(30)), "automatic confirmation rejects times before T-5");
Check(MissionAutomationService.InConfirmationWindow(scheduled.AddMinutes(-5), scheduled, scheduled.AddMinutes(30)), "automatic confirmation opens exactly at T-5");
Check(!MissionAutomationService.InConfirmationWindow(scheduled, scheduled, scheduled.AddMinutes(30)), "confirmation closes at the scheduled start");
Check(!MissionAutomationService.InConfirmationWindow(scheduled.AddMinutes(30), scheduled, scheduled.AddMinutes(30)), "confirmation closes exactly at mission end");
var reviewedPlan = new Mission { Id = Guid.NewGuid(), CreatedBy = Guid.NewGuid(), Name = "Review fingerprint" };
var manualHash = DroneOps.Application.Validators.Missions.MissionValidationService.Hash(reviewedPlan);
reviewedPlan.AutomaticFlightRequested = true;
Check(manualHash != DroneOps.Application.Validators.Missions.MissionValidationService.Hash(reviewedPlan), "automatic opt-in invalidates manual-plan approval and confirmation evidence");
reviewedPlan.AutomaticFlightRequested = false;
Check(manualHash == DroneOps.Application.Validators.Missions.MissionValidationService.Hash(reviewedPlan), "legacy manual plan fingerprints remain compatible");

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
    batteryCurrentAmps = (decimal?)12.5m, batteryTemperatureCelsius = (decimal?)31.5m,
    batteryConsumedMah = (int?)420, batteryCellVoltagesVolts = new decimal[] { 3.95m, 3.95m, 3.95m, 3.95m },
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
    Check((await client.SendAsync(Ingest("wrong-mapping", Sample(now, otherUavId)))).StatusCode == HttpStatusCode.Forbidden, "mission-to-UAV mapping mismatch is rejected");

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
    var missingResponse = await client.GetAsync($"/api/missions/{emptyMissionId}/telemetry/latest");
    Check(missingResponse.StatusCode == HttpStatusCode.OK,
        $"mission without samples endpoint succeeds ({missingResponse.StatusCode}: {await missingResponse.Content.ReadAsStringAsync()})");
    var missing = await Json(missingResponse);
    Check(missing.GetProperty("snapshotKind").GetString() == "Missing" && missing.GetProperty("sourceTimestamp").ValueKind == JsonValueKind.Null && missing.GetProperty("latitude").ValueKind == JsonValueKind.Null, "mission without samples returns the explicit Missing contract");
    var latestResponse = await client.GetAsync($"/api/missions/{missionId}/telemetry/latest");
    Check(latestResponse.StatusCode == HttpStatusCode.OK, $"manager can read latest API ({latestResponse.StatusCode})");
    var latest = await Json(latestResponse);
    Check(latest.GetProperty("missionSequence").GetInt32() == 3, "latest API keeps the newest projection");
    Check(latest.GetProperty("latitude").GetDecimal() == 10.849m && latest.GetProperty("progressPercent").ValueKind == JsonValueKind.Null, "current command sequence never fabricates reached-waypoint progress");
    Check(latest.GetProperty("headingDegrees").GetDecimal() == 215.5m && latest.GetProperty("batteryVoltageVolts").GetDecimal() == 15.8m, "optional heading and battery voltage are preserved when supplied");
    Check(latest.GetProperty("batteryCurrentAmps").GetDecimal() == 12.5m &&
          latest.GetProperty("batteryTemperatureCelsius").GetDecimal() == 31.5m &&
          latest.GetProperty("batteryConsumedMah").GetInt32() == 420 &&
          latest.GetProperty("batteryCellVoltagesVolts").GetArrayLength() == 4,
          "battery current, temperature, consumed charge and cells survive ingestion and latest API");
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

    using (var unauthorizedStream = new HttpRequestMessage(HttpMethod.Get, $"/api/live/stream?missionId={missionId}"))
    {
        unauthorizedStream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(Guid.NewGuid(), "uav_operator"));
        using var denied = await client.SendAsync(unauthorizedStream);
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "shared SSE snapshot remains forbidden to another pilot");
    }

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

    // Incident detector: một mẫu nhiễu không mở incident; mẫu vi phạm thứ hai mới mở.
    client.DefaultRequestHeaders.Authorization = null;
    var detectorAt = DateTimeOffset.UtcNow;
    await client.SendAsync(Ingest("low-battery-1", Sample(detectorAt, battery: 19, sequence: 10)));
    await using (var scope = app.Services.CreateAsyncScope())
        Check(!await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.AnyAsync(x => x.Type == "LowBattery"), "one violating sample does not raise a noisy incident");
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    var lowBatterySecond = await Json(await client.SendAsync(Ingest("low-battery-2", Sample(detectorAt.AddSeconds(1), battery: 19, sequence: 11))));
    Check(lowBatterySecond.GetProperty("raisedIncidentId").ValueKind == JsonValueKind.String, "second consecutive violating sample raises an incident");
    var lowBatteryIncidentId = lowBatterySecond.GetProperty("raisedIncidentId").GetGuid();
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(await db.Notifications.CountAsync(x =>
                x.RecipientUserId == pilotId && x.Type == "IncidentRaised" &&
                x.ReferenceId == lowBatteryIncidentId && x.ReferenceType == "Incident" &&
                x.ActionUrl == $"/incident-management?incidentId={lowBatteryIncidentId}") == 1,
            "new incident creates one navigable Facebook-style notification for the pilot");
    }

    // Pilot nhận alert bền vững và acknowledge; chỉ Manager được resolve; lịch sử append-only qua HTTP.
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(pilotId, "uav_operator"));
    var unread = await Json(await client.GetAsync("/api/incidents/alerts/unread"));
    Check(unread.GetArrayLength() >= 1, "raised incident creates a durable unread alert for the mission owner");
    var acknowledge = await client.PostAsJsonAsync($"/api/incidents/{lowBatteryIncidentId}/acknowledge", new { notes = "Pilot is handling the event.", expectedStatus = "Open", expectedRevision = 1 });
    Check(acknowledge.StatusCode == HttpStatusCode.OK && (await Json(acknowledge)).GetProperty("status").GetString() == "Acknowledged", "pilot acknowledges an incident through HTTP API");
    var resolve = await client.PostAsJsonAsync($"/api/incidents/{lowBatteryIncidentId}/resolve", new { resolutionNotes = "Battery was replaced.", expectedStatus = "Acknowledged", expectedRevision = 2 });
    Check(resolve.StatusCode == HttpStatusCode.Forbidden, "pilot cannot resolve an incident");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(managerId, "operation_manager"));
    var staleResolve = await client.PostAsJsonAsync($"/api/incidents/{lowBatteryIncidentId}/resolve", new { resolutionNotes = "Reviewed.", expectedStatus = "Open", expectedRevision = 1 });
    Check(staleResolve.StatusCode == HttpStatusCode.Conflict, "stale incident form is rejected");
    resolve = await client.PostAsJsonAsync($"/api/incidents/{lowBatteryIncidentId}/resolve", new { resolutionNotes = "Battery was replaced.", expectedStatus = "Acknowledged", expectedRevision = 2 });
    Check(resolve.StatusCode == HttpStatusCode.OK, "manager resolves with current state and reason");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(pilotId, "uav_operator"));
    var history = await Json(await client.GetAsync($"/api/incidents/{lowBatteryIncidentId}/history"));
    Check(history.GetArrayLength() == 3 && history[0].GetProperty("eventType").GetString() == "Raised", "incident history preserves raised, acknowledged and resolved transitions");
    var alertId = unread[0].GetProperty("id").GetGuid();
    Check((await client.PostAsync($"/api/incidents/alerts/{alertId}/read", null)).StatusCode == HttpStatusCode.OK, "recipient can mark a durable alert as read");

    // Manual closure is not measured recovery and must not duplicate an episode.
    client.DefaultRequestHeaders.Authorization = null;
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    await client.SendAsync(Ingest("low-battery-still-abnormal", Sample(detectorAt.AddSeconds(2), battery: 18)));
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(await db.Incidents.CountAsync(x => x.Type == "LowBattery") == 1,
            "manual resolution does not create a second incident before measured recovery");
        Check((await db.Incidents.SingleAsync(x => x.Type == "LowBattery")).Severity == "Warning",
            "low battery follows the SRS Warning severity");
    }

    // Hysteresis/recovery: vùng giữa ngưỡng không đóng incident; hai mẫu khỏe liên tiếp mới đóng.
    client.DefaultRequestHeaders.Authorization = null;
    var highSpeed1 = new IngestTelemetrySampleRequest
    {
        SourceId = "sim-uav-001", UavId = fixtureUavId, MissionId = missionId,
        SourceTimestamp = detectorAt.AddSeconds(3), Latitude = 10.849m, Longitude = 106.772m,
        GroundSpeedMetersPerSecond = 30, BatteryPercent = 70, Armed = true, MissionSequence = 13
    };
    var highSpeed2 = new IngestTelemetrySampleRequest
    {
        SourceId = "sim-uav-001", UavId = fixtureUavId, MissionId = missionId,
        SourceTimestamp = detectorAt.AddSeconds(4), Latitude = 10.849m, Longitude = 106.772m,
        GroundSpeedMetersPerSecond = 31, BatteryPercent = 70, Armed = true, MissionSequence = 14
    };
    await Task.Delay(TimeSpan.FromSeconds(3));
    await client.SendAsync(Ingest("speed-high-real-1", highSpeed1));
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    await client.SendAsync(Ingest("speed-high-real-2", highSpeed2));
    var middle = highSpeed2; middle.SourceTimestamp = detectorAt.AddSeconds(5); middle.GroundSpeedMetersPerSecond = 23; middle.MissionSequence = 15;
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    await client.SendAsync(Ingest("speed-middle", middle));
    await using (var scope = app.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.AnyAsync(x => x.Type == "SpeedExceeded" && x.Status != "Resolved"), "one within-limit sample does not end the abnormal episode");
    middle.SourceTimestamp = detectorAt.AddSeconds(6); middle.GroundSpeedMetersPerSecond = 20; middle.MissionSequence = 16;
    // Another within-limit sample completes the required two-sample recovery.
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    await client.SendAsync(Ingest("speed-recovery-1", middle));
    middle.SourceTimestamp = detectorAt.AddSeconds(7); middle.MissionSequence = 17;
    await Task.Delay(TimeSpan.FromSeconds(1.05));
    await client.SendAsync(Ingest("speed-recovery-2", middle));
    await using (var scope = app.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.AnyAsync(x => x.Type == "SpeedExceeded" && x.RecoveredAt != null), "two healthy samples record recovery without bypassing Manager resolution");

    // Mission-specific keep-in fences reuse the existing incident lifecycle.
    var fenceMissionId = Guid.NewGuid();
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Missions.Add(new Mission { Id=fenceMissionId,CreatedBy=pilotId,UAVId=fixtureUavId,Name="Mission fence check",Status="In Progress",
            StartTime=DateTime.UtcNow.AddMinutes(-5),EndTime=DateTime.UtcNow.AddMinutes(20),CreatedAt=DateTime.UtcNow,
            Waypoints=[new Waypoint {Id=Guid.NewGuid(),MissionId=fenceMissionId,SequenceOrder=1,Latitude=10.84m,Longitude=106.77m,
                Navigation=new(MissionFences:[new MissionFence("test-circle","Circle",Center:new(10.84m,106.77m),RadiusMeters:100)])}] });
        await db.SaveChangesAsync();
    }
    var fenceAt=DateTimeOffset.UtcNow.AddSeconds(-5);var fenceSequence=0;
    async Task FenceSample(decimal? latitude,decimal? longitude)
    {
        fenceSequence++;
        var response=await client.SendAsync(Ingest("mission-fence-"+fenceSequence,new IngestTelemetrySampleRequest {
            SourceId="fence-source",UavId=fixtureUavId,MissionId=fenceMissionId,SourceTimestamp=fenceAt.AddMilliseconds(fenceSequence*200),
            Latitude=latitude,Longitude=longitude,Armed=true,BatteryPercent=70,AltitudeMeters=20,GroundSpeedMetersPerSecond=2 }));
        Check(response.IsSuccessStatusCode,"mission fence telemetry accepted");
    }
    await FenceSample(10.85m,106.77m);
    await using(var scope=app.Services.CreateAsyncScope())
        Check(!await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.AnyAsync(i=>i.MissionId==fenceMissionId),"one outside sample does not raise a fence incident");
    await FenceSample(10.85m,106.77m);await FenceSample(10.85m,106.77m);
    await using(var scope=app.Services.CreateAsyncScope())
    {
        var incidents=await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.Where(i=>i.MissionId==fenceMissionId).ToListAsync();
        Check(incidents.Count==1 && incidents[0].Type=="GeofenceBreach" && incidents[0].Evidence!.Contains("test-circle"),"outside samples create one existing GeofenceBreach with frozen mission-fence evidence");
    }
    await FenceSample(10.84m,106.77m);await FenceSample(10.84m,106.77m);
    await using(var scope=app.Services.CreateAsyncScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Check(await db.Incidents.AnyAsync(i=>i.MissionId==fenceMissionId && i.RecoveredAt!=null),"two inside samples recover the mission-fence episode");
        var ended=await db.Missions.FindAsync(fenceMissionId);ended!.EndTime=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
    }
    await FenceSample(10.85m,106.77m);await FenceSample(10.85m,106.77m);
    await using(var scope=app.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.CountAsync(i=>i.MissionId==fenceMissionId)==1,"ended mission does not generate a new fence incident");

    if (args.Length == 2 && args[0] == "--gateway")
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(pilotId,"uav_operator"));
        var created=await client.PostAsJsonAsync("/api/drone-connections",new CreateDroneConnection(fixtureUavId,missionId));
        Check(created.StatusCode==HttpStatusCode.Created,"HTTP pilot connection creates a pairing code");
        var pairing=(await created.Content.ReadFromJsonAsync<DronePairing>())!;
        var python=Environment.GetEnvironmentVariable("DRONEOPS_GATEWAY_PYTHON")??"python3";
        var start=new ProcessStartInfo(python){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        start.ArgumentList.Add(Path.Combine(Path.GetFullPath(args[1]),"gateway/http_smoke.py"));start.ArgumentList.Add(address);
        using var gateway=Process.Start(start)!;
        var stdout=gateway.StandardOutput.ReadToEndAsync();var stderr=gateway.StandardError.ReadToEndAsync();
        await gateway.StandardInput.WriteLineAsync(pairing.PairingCode);gateway.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
        try{await gateway.WaitForExitAsync(timeout.Token);}catch{gateway.Kill(true);throw;}
        if(gateway.ExitCode!=0)throw new Exception("Gateway WebSocket test failed: "+await stderr);
        Check(true,"real MAVLink UDP frames pass through the Jetson gateway and WebSocket pairing");
        var gatewayLatest=await Json(await client.GetAsync($"/api/missions/{missionId}/telemetry/latest"));
        Check(gatewayLatest.GetProperty("latitude").GetDecimal()==11.35m&&gatewayLatest.GetProperty("groundSpeedMetersPerSecond").GetDecimal()==5m,"gateway measurements reach latest API with converted units");
        var sessions=(await client.GetFromJsonAsync<List<DroneConnectionView>>("/api/drone-connections"))!;
        Check(sessions.Single().Status=="ReceivingTelemetry","session API reports drone samples separately from gateway online");
        using var gatewayStreamTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var gatewayStream=await client.GetAsync($"/api/live/stream?missionId={missionId}",HttpCompletionOption.ResponseHeadersRead,gatewayStreamTimeout.Token);
        gatewayStream.EnsureSuccessStatusCode();
        using var gatewayReader=new StreamReader(await gatewayStream.Content.ReadAsStreamAsync(gatewayStreamTimeout.Token));
        var snapshot="";
        while(!snapshot.StartsWith("data:")){snapshot=await gatewayReader.ReadLineAsync(gatewayStreamTimeout.Token)??throw new Exception("Missing SSE snapshot");}
        Check(snapshot.Contains("11.35"),"Live Monitoring SSE receives the paired gateway position");
        Check((await client.DeleteAsync($"/api/drone-connections/{pairing.Connection.Id}")).StatusCode==HttpStatusCode.NoContent,"pilot can revoke gateway over HTTP");
        // The loopback suite uses InMemory EF and a repository test double, not PostgreSQL.
        var retained=(await client.GetFromJsonAsync<List<DroneBindingView>>("/api/drone-connections/aircraft"))!;
        Check(retained.Single().HardwareIdentity=="UID64-00000000075BCD15","physical hardware binding survives disconnect");
        // Wrong-hardware and duplicate-registry pairing are covered at the service boundary in AuthChecks;
        // this loopback section verifies the real WebSocket transport and MAVLink framing.
        await using(var modelScope=app.Services.CreateAsyncScope())
        {
            var entity=modelScope.ServiceProvider.GetRequiredService<AppDbContext>().Model.FindEntityType(typeof(DroneHardwareIdentity))!;
            Check(entity.GetForeignKeys().Single().Properties.Single().Name=="UavId"&&!entity.GetProperties().Any(p=>p.IsShadowProperty()),"hardware identity maps one-to-one without a shadow UAV column");
        }
        Console.WriteLine("Gateway loopback WebSocket checks passed using isolated in-memory fixtures.");
    }

    Console.WriteLine($"\n{passed} telemetry end-to-end checks passed. Fixture/simulator only; no hardware verification claimed.");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}
