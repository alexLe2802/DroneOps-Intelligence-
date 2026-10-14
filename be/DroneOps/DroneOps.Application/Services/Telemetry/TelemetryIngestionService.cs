using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.Telemetry;

public class TelemetryIngestionService : ITelemetryIngestionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TelemetryIngestionService> _logger;

    public TelemetryIngestionService(AppDbContext context, ILogger<TelemetryIngestionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> IngestSampleAsync(
        string idempotencyKey,
        IngestTelemetrySampleRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Kiểm tra Deduplication / Idempotency Key
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var isDuplicate = await _context.TelemetryRecords
                .AsNoTracking()
                .AnyAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);

            if (isDuplicate)
            {
                _logger.LogInformation("Duplicate telemetry packet ignored. Key: {Key}", idempotencyKey);
                return true;
            }
        }

        // 2. Xác thực UAV có tồn tại trong hệ thống hay không
        var uavExists = await _context.UAVs
            .AsNoTracking()
            .AnyAsync(u => u.Id == request.UavId, cancellationToken);

        if (!uavExists)
        {
            _logger.LogWarning("Telemetry rejected: UAV {UavId} does not exist.", request.UavId);
            throw new KeyNotFoundException($"UAV with ID {request.UavId} not found.");
        }

        // 3. Khởi tạo bản ghi TelemetryRecord mới
        var record = new TelemetryRecord
        {
            Id = Guid.NewGuid(),
            SourceId = request.SourceId,
            UAVId = request.UavId,
            MissionId = request.MissionId,
            SourceTimestamp = request.SourceTimestamp,
            ReceivedAt = DateTimeOffset.UtcNow,
            RecordedAt = request.SourceTimestamp,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Altitude = request.AltitudeMeters,
            AltitudeReference = request.AltitudeReference ?? "Relative",
            Speed = request.GroundSpeedMetersPerSecond,
            BatteryLevel = request.BatteryPercent,
            FlightMode = request.FlightMode,
            Armed = request.Armed,
            MissionSequence = request.MissionSequence,
            IdempotencyKey = idempotencyKey,
            IsValid = true
        };

        await _context.TelemetryRecords.AddAsync(record, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<MissionLatestTelemetryResponse?> GetLatestMissionTelemetryAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var mission = await _context.Missions
            .AsNoTracking()
            .Include(m => m.UAV)
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);

        if (mission == null) return null;

        if (!isManager && currentUserId.HasValue && mission.CreatedBy != currentUserId.Value)
        {
            throw new UnauthorizedAccessException("You do not have permission to view telemetry for this mission.");
        }

        var latestRecord = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(t => t.MissionId == missionId)
            .OrderByDescending(t => t.SourceTimestamp)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new MissionLatestTelemetryResponse
        {
            MissionId = mission.Id,
            UavId = mission.UAVId,
            UavCode = mission.UAV?.Code,
            UavName = mission.UAV?.Name
        };

        if (latestRecord == null)
        {
            response.ConnectivityStatus = "Offline";
            response.IsStale = true;
            response.IsValid = false;
            return response;
        }

        var ageSeconds = (DateTimeOffset.UtcNow - latestRecord.ReceivedAt).TotalSeconds;
        response.DataAgeSeconds = Math.Round(ageSeconds, 1);
        response.IsStale = ageSeconds > 15.0;
        response.ConnectivityStatus = ageSeconds switch
        {
            <= 10.0 => "Online",
            <= 30.0 => "Stale",
            _ => "Offline"
        };

        response.SourceId = latestRecord.SourceId;
        response.Latitude = latestRecord.Latitude;
        response.Longitude = latestRecord.Longitude;
        response.AltitudeMeters = latestRecord.Altitude;
        response.AltitudeReference = latestRecord.AltitudeReference;
        response.GroundSpeedMetersPerSecond = latestRecord.Speed;
        response.BatteryPercent = latestRecord.BatteryLevel;
        response.FlightMode = latestRecord.FlightMode;
        response.Armed = latestRecord.Armed;
        response.MissionSequence = latestRecord.MissionSequence;
        response.SourceTimestamp = latestRecord.SourceTimestamp;
        response.ReceivedAt = latestRecord.ReceivedAt;
        response.IsValid = latestRecord.IsValid;

        return response;
    }

    public async Task<List<LiveMissionItemResponse>> GetLiveMissionsAsync(
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        // 1. Lọc các nhiệm vụ đang hoạt động (không bao gồm nhiệm vụ đã chốt Completed hoặc Cancelled)
        var query = _context.Missions
            .AsNoTracking()
            .Include(m => m.UAV)
            .Include(m => m.Creator)
            .Where(m => m.Status != "Completed" && m.Status != "Cancelled");

        // Pilot chỉ xem các nhiệm vụ của chính mình
        if (!isManager && currentUserId.HasValue)
        {
            query = query.Where(m => m.CreatedBy == currentUserId.Value);
        }

        var missions = await query
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new List<LiveMissionItemResponse>();

        // 2. Ghép dữ liệu telemetry tức thời cho từng nhiệm vụ đang hoạt động
        foreach (var mission in missions)
        {
            var latest = await _context.TelemetryRecords
                .AsNoTracking()
                .Where(t => t.MissionId == mission.Id)
                .OrderByDescending(t => t.SourceTimestamp)
                .FirstOrDefaultAsync(cancellationToken);

            var item = new LiveMissionItemResponse
            {
                MissionId = mission.Id,
                MissionName = mission.Name,
                MissionStatus = mission.Status,
                StartTime = mission.StartTime,
                UavId = mission.UAVId,
                UavCode = mission.UAV?.Code,
                UavName = mission.UAV?.Name,
                PilotId = mission.CreatedBy,
                PilotName = mission.Creator?.FullName ?? mission.Creator?.Email
            };

            if (latest != null)
            {
                var ageSeconds = (DateTimeOffset.UtcNow - latest.ReceivedAt).TotalSeconds;
                item.DataAgeSeconds = Math.Round(ageSeconds, 1);
                item.LastSeen = latest.ReceivedAt;
                item.ConnectivityStatus = ageSeconds switch
                {
                    <= 10.0 => "Online",
                    <= 30.0 => "Stale",
                    _ => "Offline"
                };

                item.Latitude = latest.Latitude;
                item.Longitude = latest.Longitude;
                item.AltitudeMeters = latest.Altitude;
                item.GroundSpeedMetersPerSecond = latest.Speed;
                item.BatteryPercent = latest.BatteryLevel;
                item.FlightMode = latest.FlightMode;
                item.Armed = latest.Armed;
                item.MissionSequence = latest.MissionSequence;
            }

            result.Add(item);
        }

        return result;
    }
}