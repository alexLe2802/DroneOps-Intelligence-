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
    private readonly ITelemetryIngestionService _telemetryService;
    private readonly IIncidentService _incidentService;

    public LiveStreamController(
        AppDbContext context,
        ITelemetryIngestionService telemetryService,
        IIncidentService incidentService)
    {
        _context = context;
        _telemetryService = telemetryService;
        _incidentService = incidentService;
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
        var knownIncidentStatus = new Dictionary<Guid, string>();
        DateTimeOffset incidentWatermark = DateTimeOffset.UtcNow.AddHours(-24);

        try
        {
            var initialSnapshot = await _telemetryService.GetLatestMissionTelemetryAsync(
                missionId, currentUserId, isManager, cancellationToken);

            if (initialSnapshot != null)
            {
                await WriteSseEventAsync("telemetryUpdated", eventSequence++, initialSnapshot, cancellationToken);
                lastSentTelemetryTime = initialSnapshot.SourceTimestamp;
                lastSentSnapshotKind = initialSnapshot.SnapshotKind;
            }

            var openIncidents = await _incidentService.GetMissionIncidentFeedAsync(
                missionId, currentUserId, isManager, changedAfter: null, cancellationToken);

            foreach (var incident in openIncidents.Where(i => i.Status != "Resolved"))
            {
                var eventName = incident.Status == "Open" ? "incidentRaised" : "incidentUpdated";
                await WriteSseEventAsync(eventName, eventSequence++, incident, cancellationToken);
                knownIncidentStatus[incident.Id] = incident.Status;
                if (incident.CreatedAt > incidentWatermark)
                {
                    incidentWatermark = incident.CreatedAt;
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1500, cancellationToken);

                var currentTelemetry = await _telemetryService.GetLatestMissionTelemetryAsync(
                    missionId, currentUserId, isManager, cancellationToken);

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

                var changedIncidents = await _incidentService.GetMissionIncidentFeedAsync(
                    missionId, currentUserId, isManager, incidentWatermark, cancellationToken);

                foreach (var incident in changedIncidents)
                {
                    var isNew = !knownIncidentStatus.ContainsKey(incident.Id);
                    var eventName = isNew && incident.Status == "Open" ? "incidentRaised" : "incidentUpdated";
                    await WriteSseEventAsync(eventName, eventSequence++, incident, cancellationToken);
                    knownIncidentStatus[incident.Id] = incident.Status;

                    var latestChange = MaxTime(incident.CreatedAt, incident.AcknowledgedAt, incident.ResolvedAt);
                    if (latestChange > incidentWatermark)
                    {
                        incidentWatermark = latestChange;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client đóng tab / ngắt SSE: vòng lặp kết thúc, không giữ subscription rò rỉ giữa các scope.
        }
    }

    private async Task WriteSseEventAsync(string eventName, long id, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, SseJson);
        var sseMessage = $"id: {id}\nevent: {eventName}\ndata: {json}\n\n";
        await Response.WriteAsync(sseMessage, ct);
        await Response.Body.FlushAsync(ct);
    }

    private static DateTimeOffset MaxTime(DateTimeOffset created, DateTimeOffset? acknowledged, DateTimeOffset? resolved)
    {
        var max = created;
        if (acknowledged.HasValue && acknowledged.Value > max)
        {
            max = acknowledged.Value;
        }

        if (resolved.HasValue && resolved.Value > max)
        {
            max = resolved.Value;
        }

        return max;
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
