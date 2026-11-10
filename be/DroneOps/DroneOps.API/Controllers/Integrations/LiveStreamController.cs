using System.Security.Claims;
using System.Text.Json;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API.Controllers.Integrations;

[ApiController]
[Route("api/live/stream")]
[Authorize(Roles = "Pilot,uav_operator,operation_manager,operations_manager")]
public class LiveStreamController : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _context;
    private readonly MissionStreamSnapshots _snapshots;

    public LiveStreamController(AppDbContext context, MissionStreamSnapshots snapshots)
    {
        _context = context;
        _snapshots = snapshots;
    }

    /// <summary>
    /// Luồng SSE: telemetry + sự cố theo mission. Không phải video livestream. Tái kết nối gửi snapshot.
    /// - Operation Manager: Được quyền xem và giám sát tất cả các mission stream.
    /// - Pilot (uav_operator): Chỉ được xem và giám sát mission stream do chính mình tạo/đăng ký.
    /// </summary>
    [HttpGet]
    [Produces("text/event-stream")]
    public async Task GetLiveStream([FromQuery] Guid missionId, CancellationToken cancellationToken)
    {
        if (missionId == Guid.Empty)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "Query parameter missionId is required." }, cancellationToken);
            return;
        }

        var mission = await _context.Missions
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { message = "Mission not found." }, cancellationToken);
            return;
        }

        var isManager = IsOperationManager();
        Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

        // Chỉ Operation Manager được xem tất cả; Pilot chỉ được xem mission do chính mình đăng ký (CreatedBy)
        if (!isManager && (!currentUserId.HasValue || mission.CreatedBy != currentUserId.Value))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(new { message = "Forbidden: You do not own this mission stream." }, cancellationToken);
            return;
        }

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        long eventSequence = 1;
        DateTimeOffset? lastSentTelemetryTime = null;
        string? lastSentSnapshotKind = null;
        var knownIncidents = new Dictionary<Guid, string>();
        using var subscription = _snapshots.Subscribe(missionId);

        LiveStreamDiagnostics.Opened();
        try
        {
            var initial = await subscription.ReadAsync(cancellationToken);
            if (initial.Telemetry is { } initialSnapshot)
            {
                await WriteSseEventAsync("telemetryUpdated", eventSequence++, initialSnapshot, cancellationToken);
                lastSentTelemetryTime = initialSnapshot.SourceTimestamp;
                lastSentSnapshotKind = initialSnapshot.SnapshotKind;
            }
            foreach (var incident in initial.Incidents)
            {
                knownIncidents[incident.Id] = JsonSerializer.Serialize(incident, SseJson);
                if (incident.Status == "Resolved") continue;
                await WriteSseEventAsync(incident.Status == "Open" ? "incidentRaised" : "incidentUpdated",
                    eventSequence++, incident, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1500, cancellationToken);

                var snapshot = await subscription.ReadAsync(cancellationToken);
                var currentTelemetry = snapshot.Telemetry;

                // Freshness is time-dependent. Emit on both a new packet and an Online→Stale→Offline
                // transition so an idle stream never leaves the browser showing cached data as fresh.
                if (currentTelemetry != null &&
                    (currentTelemetry.SourceTimestamp != lastSentTelemetryTime ||
                     currentTelemetry.SnapshotKind != lastSentSnapshotKind))
                {
                    await WriteSseEventAsync("telemetryUpdated", eventSequence++, currentTelemetry, cancellationToken);
                    lastSentTelemetryTime = currentTelemetry.SourceTimestamp;
                    lastSentSnapshotKind = currentTelemetry.SnapshotKind;
                }
                else
                {
                    await Response.WriteAsync($": keep-alive {DateTimeOffset.UtcNow:O}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                foreach (var incident in snapshot.Incidents)
                {
                    var serialized = JsonSerializer.Serialize(incident, SseJson);
                    var isNew = !knownIncidents.TryGetValue(incident.Id, out var previous);
                    if (serialized == previous) continue;
                    var eventName = isNew && incident.Status == "Open" ? "incidentRaised" : "incidentUpdated";
                    await WriteSseEventAsync(eventName, eventSequence++, incident, cancellationToken);
                    knownIncidents[incident.Id] = serialized;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client đóng tab / ngắt SSE: vòng lặp kết thúc, không giữ subscription rò rỉ giữa các scope.
        }
        finally
        {
            LiveStreamDiagnostics.Closed();
        }
    }

    [HttpGet("diagnostics")]
    public IActionResult GetDiagnostics() => Ok(LiveStreamDiagnostics.Snapshot());

    private async Task WriteSseEventAsync(string eventName, long id, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, SseJson);
        var sseMessage = $"id: {id}\nevent: {eventName}\ndata: {json}\n\n";
        await Response.WriteAsync(sseMessage, ct);
        await Response.Body.FlushAsync(ct);
    }

    private bool IsOperationManager()
    {
        if (User.IsInRole("operation_manager") || User.IsInRole("operations_manager"))
        {
            return true;
        }

        var roleClaims = User.FindAll(ClaimTypes.Role)
            .Concat(User.FindAll("role"))
            .Select(c => c.Value);

        return roleClaims.Any(r =>
            string.Equals(r, "operation_manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "operations_manager", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
