using System.Security.Claims;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.Incidents;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API.Controllers.Integrations;

[ApiController]
[Route("api/live/stream")]
[Authorize(Roles = "Pilot,operation_manager,uav_operator,operations_manager")]
public class LiveStreamController : ControllerBase
{
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
    /// Luồng Server-Sent Events (SSE) phát dữ liệu viễn trắc và sự cố thời gian thực theo nhiệm vụ (SCR-10 / UC-14 / KAN-36)
    /// </summary>
    [HttpGet]
    [Produces("text/event-stream")]
    public async Task GetLiveStream([FromQuery] Guid missionId, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra tồn tại và phân quyền nhiệm vụ
        var mission = await _context.Missions
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsync("Mission not found.", cancellationToken);
            return;
        }

        var isManager = User.IsInRole("operation_manager") || User.IsInRole("operations_manager");
        Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

        if (!isManager && currentUserId.HasValue && mission.CreatedBy != currentUserId.Value)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsync("Forbidden: You do not own this mission stream.", cancellationToken);
            return;
        }

        // 2. Thiết lập header SSE chuẩn
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        long eventSequence = 1;
        DateTimeOffset? lastSentTelemetryTime = null;

        try
        {
            // 3. Khi vừa kết nối: Gửi ngay snapshot viễn trắc mới nhất và danh sách sự cố chưa xử lý (Reconnect/Resync)
            var initialSnapshot = await _telemetryService.GetLatestMissionTelemetryAsync(
                missionId, currentUserId, isManager, cancellationToken);

            if (initialSnapshot != null)
            {
                await WriteSseEventAsync("telemetryUpdated", eventSequence++, initialSnapshot, cancellationToken);
                lastSentTelemetryTime = initialSnapshot.SourceTimestamp;
            }

            var openIncidents = await _incidentService.GetIncidentsAsync(
                new GetIncidentsRequest { MissionId = missionId, Status = "Open", PageSize = 20 },
                currentUserId, isManager, cancellationToken);

            foreach (var incident in openIncidents.Items)
            {
                await WriteSseEventAsync("incidentRaised", eventSequence++, incident, cancellationToken);
            }

            // 4. Vòng lặp duy trì kết nối (Keep-Alive Heartbeat) và stream dữ liệu mới
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(2000, cancellationToken); // Nhịp kiểm tra mỗi 2 giây

                var currentTelemetry = await _telemetryService.GetLatestMissionTelemetryAsync(
                    missionId, currentUserId, isManager, cancellationToken);

                if (currentTelemetry != null && currentTelemetry.SourceTimestamp != lastSentTelemetryTime)
                {
                    await WriteSseEventAsync("telemetryUpdated", eventSequence++, currentTelemetry, cancellationToken);
                    lastSentTelemetryTime = currentTelemetry.SourceTimestamp;
                }
                else
                {
                    // Gửi nhịp keep-alive để giữ vững kết nối SSE không bị ngắt bởi trình duyệt hoặc proxy
                    await Response.WriteAsync($": keep-alive {DateTimeOffset.UtcNow:O}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Trình duyệt của Frontend ngắt kết nối hoặc đóng tab -> tự động dọn dẹp và giải phóng tài nguyên sạch sẽ
        }
    }

    private async Task WriteSseEventAsync(string eventName, long id, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        var sseMessage = $"id: {id}\nevent: {eventName}\ndata: {json}\n\n";
        await Response.WriteAsync(sseMessage, ct);
        await Response.Body.FlushAsync(ct);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}