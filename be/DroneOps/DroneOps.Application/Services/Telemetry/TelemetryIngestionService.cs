using System.Text.Json;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Geofences;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.Telemetry;

public class TelemetryIngestionService : ITelemetryIngestionService
{
    private const int ClockSkewGraceSeconds = 120;

    private static readonly string[] ActiveMissionStatuses =
    {
        "Approved",
        "In Progress",
        "InProgress",
        "Active",
        "Flying",
        "Executing"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _context;
    private readonly ILogger<TelemetryIngestionService> _logger;
    private readonly TelemetryMonitoringOptions _monitoring;
    private readonly IIncidentLifecycleService _incidentLifecycle;
    private readonly INotificationService? _notificationService;

    public TelemetryIngestionService(
        AppDbContext context,
        ILogger<TelemetryIngestionService> logger,
        IOptions<TelemetryMonitoringOptions> monitoring,
        IIncidentLifecycleService incidentLifecycle,
        INotificationService? notificationService = null)
    {
        _context = context;
        _logger = logger;
        _monitoring = monitoring.Value;
        _incidentLifecycle = incidentLifecycle;
        _notificationService = notificationService;
    }

    public async Task<TelemetryIngestResult> IngestSampleAsync(
        string idempotencyKey,
        IngestTelemetrySampleRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Idempotency: gói trùng trả thành công mà không ghi thêm (retry an toàn).
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _context.TelemetryRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existing != null)
            {
                var hasNewerValidSample = existing.MissionId.HasValue && await _context.TelemetryRecords
                    .AsNoTracking()
                    .AnyAsync(t => t.MissionId == existing.MissionId &&
                                   t.IsValid &&
                                   t.SourceTimestamp > existing.SourceTimestamp,
                        cancellationToken);
                _logger.LogInformation("Duplicate telemetry packet ignored. Key hash length: {Length}", idempotencyKey.Length);
                return new TelemetryIngestResult
                {
                    Accepted = true,
                    IsDuplicate = true,
                    IsLateArrival = hasNewerValidSample,
                    RecordId = existing.Id,
                    UsedForLatestProjection = existing.IsValid && !hasNewerValidSample,
                    Message = "Duplicate telemetry sample ignored (idempotent replay)."
                };
            }
        }

        var uavExists = await _context.UAVs
            .AsNoTracking()
            .AnyAsync(u => u.Id == request.UavId, cancellationToken);

        if (!uavExists)
        {
            _logger.LogWarning("Telemetry rejected: unknown UAV {UavId}.", request.UavId);
            throw new KeyNotFoundException($"UAV with ID {request.UavId} not found.");
        }

        Guid? missionId = request.MissionId;
        if (missionId.HasValue)
        {
            var mission = await _context.Missions
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == missionId.Value, cancellationToken);

            if (mission == null)
            {
                throw new KeyNotFoundException($"Mission with ID {missionId.Value} not found.");
            }

            // Chối gói khi UAV trên payload không khớp UAV được gán cho nhiệm vụ.
            if (mission.UAVId.HasValue && mission.UAVId.Value != request.UavId)
            {
                throw new ArgumentException("Telemetry source UAV does not match the assigned mission UAV.");
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Npgsql chỉ ghi timestamp with time zone bằng UTC. Gateway/Swagger có thể gửi
        // múi giờ địa phương (+07:00), vì vậy luôn chuẩn hóa trước khi so sánh và lưu DB.
        var sourceTimestampUtc = request.SourceTimestamp.ToUniversalTime();
        var clockSkew = sourceTimestampUtc - now;
        var isFutureSkew = clockSkew > TimeSpan.FromSeconds(ClockSkewGraceSeconds);

        DateTimeOffset? currentLatestSource = null;
        if (missionId.HasValue)
        {
            currentLatestSource = await _context.TelemetryRecords
                .AsNoTracking()
                .Where(t => t.MissionId == missionId.Value && t.IsValid)
                .MaxAsync(t => (DateTimeOffset?)t.SourceTimestamp, cancellationToken);
        }

        // Gói đến muộn vẫn lưu lịch sử, nhưng không được ghi đè projection "latest".
        var isLateArrival = currentLatestSource.HasValue && sourceTimestampUtc < currentLatestSource.Value;
        var usedForLatest = !isFutureSkew && !isLateArrival;

        var record = new TelemetryRecord
        {
            Id = Guid.NewGuid(),
            SourceId = request.SourceId,
            UAVId = request.UavId,
            MissionId = missionId,
            SourceTimestamp = sourceTimestampUtc,
            ReceivedAt = now,
            RecordedAt = sourceTimestampUtc,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Altitude = request.AltitudeMeters,
            AltitudeReference = string.IsNullOrWhiteSpace(request.AltitudeReference)
                ? "Relative"
                : request.AltitudeReference,
            Speed = request.GroundSpeedMetersPerSecond,
            HeadingDegrees = request.HeadingDegrees,
            BatteryLevel = request.BatteryPercent,
            BatteryVoltageVolts = request.BatteryVoltageVolts,
            BoardTemperatureCelsius = request.BoardTemperatureCelsius,
            FlightMode = string.IsNullOrWhiteSpace(request.FlightMode) ? null : request.FlightMode.Trim(),
            Armed = request.Armed,
            MissionSequence = request.MissionSequence,
            IdempotencyKey = idempotencyKey,
            IsValid = !isFutureSkew
        };

        await _context.TelemetryRecords.AddAsync(record, cancellationToken);

        var raisedIncidentIds = new List<Guid>();
        if (usedForLatest && record.IsValid && missionId.HasValue)
        {
            raisedIncidentIds = await TryRaiseOperationalIncidentsAsync(record, cancellationToken);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // A parallel retry can pass the read check before the first request commits.
            // The database unique index is authoritative; report its winner as a replay.
            _context.Entry(record).State = EntityState.Detached;
            var winner = await _context.TelemetryRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);
            if (winner == null)
            {
                throw;
            }

            return new TelemetryIngestResult
            {
                Accepted = true,
                IsDuplicate = true,
                RecordId = winner.Id,
                UsedForLatestProjection = winner.IsValid,
                Message = "Duplicate telemetry sample ignored (concurrent idempotent replay)."
            };
        }

        await TryPublishIncidentNotificationsAsync(raisedIncidentIds, cancellationToken);

        return new TelemetryIngestResult
        {
            Accepted = true,
            IsDuplicate = false,
            IsLateArrival = isLateArrival,
            UsedForLatestProjection = usedForLatest && record.IsValid,
            RecordId = record.Id,
            RaisedIncidentId = raisedIncidentIds.Count == 0 ? null : raisedIncidentIds[0],
            Message = isLateArrival
                ? "Late telemetry sample stored for history and did not replace the latest projection."
                : "Telemetry sample ingested successfully."
        };
    }

    public async Task<MissionLatestTelemetryResponse?> GetLatestMissionTelemetryAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var mission = await _context.Missions
            .AsNoTracking()
            .Where(m => m.Id == missionId)
            .Select(m => new
            {
                m.Id,
                m.UAVId,
                m.CreatedBy,
                UavCode = m.UAV != null ? m.UAV.Code : null,
                UavName = m.UAV != null ? m.UAV.Name : null,
                WaypointTotal = m.Waypoints.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (mission == null)
        {
            return null;
        }

        if (!isManager && (!currentUserId.HasValue || mission.CreatedBy != currentUserId.Value))
        {
            throw new UnauthorizedAccessException("You do not have permission to view telemetry for this mission.");
        }

        var latestRecord = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(t => t.MissionId == missionId && t.IsValid)
            .OrderByDescending(t => t.SourceTimestamp)
            .ThenByDescending(t => t.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new MissionLatestTelemetryResponse
        {
            MissionId = mission.Id,
            UavId = mission.UAVId,
            UavCode = mission.UavCode,
            UavName = mission.UavName,
            WaypointTotal = mission.WaypointTotal
        };

        if (latestRecord == null)
        {
            response.ConnectivityStatus = "Offline";
            response.IsStale = true;
            response.IsValid = false;
            response.SnapshotKind = "Missing";
            return response;
        }

        ApplyFreshness(response, latestRecord.ReceivedAt);
        response.SourceId = latestRecord.SourceId;
        response.Latitude = latestRecord.Latitude;
        response.Longitude = latestRecord.Longitude;
        response.AltitudeMeters = latestRecord.Altitude;
        response.AltitudeReference = latestRecord.AltitudeReference;
        response.GroundSpeedMetersPerSecond = latestRecord.Speed;
        response.HeadingDegrees = latestRecord.HeadingDegrees;
        response.BatteryPercent = latestRecord.BatteryLevel;
        response.BatteryVoltageVolts = latestRecord.BatteryVoltageVolts;
        response.BoardTemperatureCelsius = latestRecord.BoardTemperatureCelsius;
        response.FlightMode = latestRecord.FlightMode;
        response.Armed = latestRecord.Armed;
        response.MissionSequence = latestRecord.MissionSequence;
        response.SourceTimestamp = latestRecord.SourceTimestamp;
        response.ReceivedAt = latestRecord.ReceivedAt;
        response.IsValid = latestRecord.IsValid;
        response.ProgressPercent = ComputeProgress(latestRecord.MissionSequence, mission.WaypointTotal);
        return response;
    }

    public async Task<List<LiveMissionItemResponse>> GetLiveMissionsAsync(
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var recentCutoff = DateTimeOffset.UtcNow.AddMinutes(-30);

        // Nhiệm vụ đang bay HOẶC vừa có telemetry tươi — loại Draft/Rejected/Completed.
        var query = _context.Missions
            .AsNoTracking()
            .Where(m =>
                m.Status != "Draft" &&
                m.Status != "Rejected" &&
                m.Status != "Completed" &&
                m.Status != "Cancelled")
            .Where(m =>
                ActiveMissionStatuses.Contains(m.Status) ||
                _context.TelemetryRecords.Any(t =>
                    t.MissionId == m.Id &&
                    t.IsValid &&
                    t.ReceivedAt >= recentCutoff));

        if (!isManager && currentUserId.HasValue)
        {
            query = query.Where(m => m.CreatedBy == currentUserId.Value);
        }

        var missions = await query
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Status,
                m.StartTime,
                m.UAVId,
                m.CreatedBy,
                UavCode = m.UAV != null ? m.UAV.Code : null,
                UavName = m.UAV != null ? m.UAV.Name : null,
                PilotName = m.Creator != null ? m.Creator.FullName ?? m.Creator.Email : null,
                WaypointTotal = m.Waypoints.Count
            })
            .ToListAsync(cancellationToken);

        if (missions.Count == 0)
        {
            return new List<LiveMissionItemResponse>();
        }

        var missionIds = missions.Select(m => m.Id).ToList();

        // Một truy vấn lấy snapshot mới nhất cho cả danh sách — tránh N+1.
        var latestRecords = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(t => t.MissionId != null && missionIds.Contains(t.MissionId.Value) && t.IsValid)
            .Where(t => t.SourceTimestamp == _context.TelemetryRecords
                .Where(inner => inner.MissionId == t.MissionId && inner.IsValid)
                .Max(inner => inner.SourceTimestamp))
            .ToListAsync(cancellationToken);

        var latestByMission = latestRecords
            .GroupBy(t => t.MissionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ReceivedAt).First());

        var result = new List<LiveMissionItemResponse>(missions.Count);
        foreach (var mission in missions)
        {
            var item = new LiveMissionItemResponse
            {
                MissionId = mission.Id,
                MissionName = mission.Name,
                MissionStatus = mission.Status,
                StartTime = mission.StartTime,
                UavId = mission.UAVId,
                UavCode = mission.UavCode,
                UavName = mission.UavName,
                PilotId = mission.CreatedBy,
                PilotName = mission.PilotName,
                WaypointTotal = mission.WaypointTotal
            };

            if (latestByMission.TryGetValue(mission.Id, out var latest))
            {
                var ageSeconds = (DateTimeOffset.UtcNow - latest.ReceivedAt).TotalSeconds;
                item.DataAgeSeconds = Math.Round(ageSeconds, 1);
                item.LastSeen = latest.ReceivedAt;
                item.ConnectivityStatus = ClassifyConnectivity(ageSeconds);
                item.Latitude = latest.Latitude;
                item.Longitude = latest.Longitude;
                item.AltitudeMeters = latest.Altitude;
                item.GroundSpeedMetersPerSecond = latest.Speed;
                item.HeadingDegrees = latest.HeadingDegrees;
                item.BatteryPercent = latest.BatteryLevel;
                item.BatteryVoltageVolts = latest.BatteryVoltageVolts;
                item.BoardTemperatureCelsius = latest.BoardTemperatureCelsius;
                item.FlightMode = latest.FlightMode;
                item.Armed = latest.Armed;
                item.MissionSequence = latest.MissionSequence;
                item.ProgressPercent = ComputeProgress(latest.MissionSequence, mission.WaypointTotal);
            }

            result.Add(item);
        }

        return result;
    }

    private async Task<List<Guid>> TryRaiseOperationalIncidentsAsync(
        TelemetryRecord record,
        CancellationToken cancellationToken)
    {
        if (!record.MissionId.HasValue)
        {
            return [];
        }

        var missionId = record.MissionId.Value;
        var raisedIncidentIds = new List<Guid>();

        // Detector dùng mẫu ngay trước đó trong DB cùng mẫu hiện tại. Hai mẫu liên tiếp
        // giúp loại nhiễu tức thời; ngưỡng recovery tách khỏi ngưỡng raise tạo hysteresis.
        var previous = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(x => x.MissionId == missionId && x.IsValid && x.SourceTimestamp < record.SourceTimestamp)
            .OrderByDescending(x => x.SourceTimestamp)
            .FirstOrDefaultAsync(cancellationToken);

        var raised = await EvaluateNumericRuleAsync(
            record, previous, "LowBattery", "Low battery detected",
            record.BatteryLevel <= 10 ? "Critical" : "High",
            record.BatteryLevel, previous?.BatteryLevel,
            value => value <= _monitoring.LowBatteryPercent,
            value => value >= _monitoring.LowBatteryPercent + _monitoring.LowBatteryRecoveryMarginPercent,
            $"Battery dropped to {record.BatteryLevel}%.", "LowBattery", _monitoring.LowBatteryPercent,
            cancellationToken);
        if (raised.HasValue) raisedIncidentIds.Add(raised.Value);

        raised = await EvaluateNumericRuleAsync(
            record, previous, "AltitudeExceeded", "Altitude limit exceeded", "High",
            record.Altitude, previous?.Altitude,
            value => value > _monitoring.MaximumAltitudeMeters,
            value => value <= _monitoring.MaximumAltitudeMeters - _monitoring.AltitudeRecoveryMarginMeters,
            $"Altitude reached {record.Altitude} m.", "MaximumAltitude", (double)_monitoring.MaximumAltitudeMeters,
            cancellationToken);
        if (raised.HasValue) raisedIncidentIds.Add(raised.Value);

        raised = await EvaluateNumericRuleAsync(
            record, previous, "SpeedExceeded", "Ground speed limit exceeded", "High",
            record.Speed, previous?.Speed,
            value => value > _monitoring.MaximumGroundSpeedMetersPerSecond,
            value => value <= _monitoring.MaximumGroundSpeedMetersPerSecond - _monitoring.GroundSpeedRecoveryMarginMetersPerSecond,
            $"Ground speed reached {record.Speed} m/s.", "MaximumGroundSpeed",
            (double)_monitoring.MaximumGroundSpeedMetersPerSecond, cancellationToken);
        if (raised.HasValue) raisedIncidentIds.Add(raised.Value);

        if (record.Latitude.HasValue && record.Longitude.HasValue)
        {
            var geofences = await _context.Geofences
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var validPolygons = new List<(string Name, List<GeofenceCoordinateResponse> Polygon)>();
            foreach (var geofence in geofences)
            {
                var polygon = JsonSerializer.Deserialize<List<GeofenceCoordinateResponse>>(
                    geofence.Coordinates,
                    JsonOptions);

                if (polygon == null || polygon.Count < 3)
                {
                    continue;
                }

                validPolygons.Add((geofence.Name, polygon));
            }

            // Geofence = vùng được phép bay: ra ngoài TẤT CẢ polygon thì mới coi là vi phạm.
            if (validPolygons.Count > 0)
            {
                var insideAny = validPolygons.Exists(g =>
                    IsPointInPolygon(
                        (double)record.Latitude.Value,
                        (double)record.Longitude.Value,
                        g.Polygon));

                var previousInside = previous?.Latitude.HasValue == true && previous.Longitude.HasValue &&
                    validPolygons.Exists(g => IsPointInPolygon(
                        (double)previous.Latitude.Value, (double)previous.Longitude.Value, g.Polygon));

                if (!insideAny && previous != null && !previousInside)
                {
                    raised = await RaiseIncidentAsync(
                        record, "GeofenceBreach", "Geofence breach detected", "High",
                        "UAV is outside every configured operating geofence.", "GeofenceKeepIn", null,
                        cancellationToken);
                    if (raised.HasValue) raisedIncidentIds.Add(raised.Value);
                }
                else if (insideAny && previousInside)
                    await _incidentLifecycle.ResolveOpenByRuleAsync(
                        missionId, "GeofenceBreach", "UAV returned inside an operating geofence for two consecutive samples.", cancellationToken);
            }
        }

        // Communication-loss chỉ tự phục hồi sau hai gói mới liên tiếp kể từ lúc incident mở.
        var communicationIncident = await _context.Incidents
            .AsNoTracking()
            .Where(x => x.MissionId == missionId && x.Type == "CommunicationLoss" && x.Status != "Resolved")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (communicationIncident != null && previous != null && previous.ReceivedAt >= communicationIncident.CreatedAt)
            await _incidentLifecycle.ResolveOpenByRuleAsync(
                missionId, "CommunicationLoss", "Telemetry communication recovered with two consecutive samples.", cancellationToken);

        return raisedIncidentIds;
    }

    private async Task TryPublishIncidentNotificationsAsync(
        IReadOnlyCollection<Guid> incidentIds, CancellationToken cancellationToken)
    {
        if (_notificationService is null || incidentIds.Count == 0) return;
        var incidents = await _context.Incidents
            .AsNoTracking()
            .Where(x => incidentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type, PilotUserId = x.Mission.CreatedBy })
            .ToListAsync(cancellationToken);
        foreach (var incident in incidents)
        {
            try
            {
                await _notificationService.PublishIncidentRaisedAsync(
                    incident.Id, incident.Type, incident.PilotUserId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception,
                    "Could not publish incident notification for {IncidentId}.", incident.Id);
            }
        }
    }

    private async Task<Guid?> EvaluateNumericRuleAsync(
        TelemetryRecord record,
        TelemetryRecord? previous,
        string type,
        string title,
        string severity,
        decimal? currentValue,
        decimal? previousValue,
        Func<decimal, bool> isViolation,
        Func<decimal, bool> isRecovered,
        string description,
        string appliedRule,
        double? threshold,
        CancellationToken cancellationToken)
    {
        if (currentValue.HasValue && previousValue.HasValue &&
            isViolation(currentValue.Value) && isViolation(previousValue.Value))
            return await RaiseIncidentAsync(
                record, type, title, severity, description, appliedRule, threshold, cancellationToken);

        if (currentValue.HasValue && previousValue.HasValue &&
            isRecovered(currentValue.Value) && isRecovered(previousValue.Value))
            await _incidentLifecycle.ResolveOpenByRuleAsync(
                record.MissionId!.Value, type,
                $"{type} condition recovered for two consecutive telemetry samples.", cancellationToken);

        return null;
    }

    private Task<Guid?> RaiseIncidentAsync(
        TelemetryRecord record, string type, string title, string severity, string description,
        string appliedRule, double? threshold, CancellationToken cancellationToken)
    {

        var evidence = JsonSerializer.Serialize(new
        {
            sourceId = record.SourceId,
            latitude = record.Latitude,
            longitude = record.Longitude,
            altitude = record.Altitude,
            speed = record.Speed,
            headingDegrees = record.HeadingDegrees,
            batteryPercent = record.BatteryLevel,
            batteryVoltageVolts = record.BatteryVoltageVolts,
            boardTemperatureCelsius = record.BoardTemperatureCelsius,
            flightMode = record.FlightMode,
            sourceTimestamp = record.SourceTimestamp,
            receivedAt = record.ReceivedAt,
            appliedRule,
            threshold
        }, JsonOptions);

        return _incidentLifecycle.RaiseIfOpenMissingAsync(new Incident
        {
            Id = Guid.NewGuid(),
            MissionId = record.MissionId!.Value,
            Title = title,
            Description = description,
            Type = type,
            Severity = severity,
            Status = "Open",
            Latitude = record.Latitude,
            Longitude = record.Longitude,
            Evidence = evidence,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private void ApplyFreshness(MissionLatestTelemetryResponse response, DateTimeOffset receivedAt)
    {
        var ageSeconds = (DateTimeOffset.UtcNow - receivedAt).TotalSeconds;
        response.DataAgeSeconds = Math.Round(ageSeconds, 1);
        response.ConnectivityStatus = ClassifyConnectivity(ageSeconds);
        response.IsStale = ageSeconds > StaleAfterSeconds;
        response.SnapshotKind = response.ConnectivityStatus switch
        {
            "Online" => "Latest",
            "Stale" => "Stale",
            _ => "Offline"
        };
    }

    private string ClassifyConnectivity(double ageSeconds) => ageSeconds switch
    {
        <= 0 => "Online",
        _ when ageSeconds <= StaleAfterSeconds => "Online",
        _ when ageSeconds <= OfflineAfterSeconds => "Stale",
        _ => "Offline"
    };

    private int StaleAfterSeconds => Math.Max(1, _monitoring.StaleAfterSeconds);

    private int OfflineAfterSeconds => Math.Max(StaleAfterSeconds + 1, _monitoring.OfflineAfterSeconds);

    private static double? ComputeProgress(int? missionSequence, int waypointTotal)
    {
        if (!missionSequence.HasValue || waypointTotal <= 0)
        {
            return null;
        }

        var percent = 100.0 * missionSequence.Value / waypointTotal;
        return Math.Round(Math.Clamp(percent, 0, 100), 1);
    }

    private static bool IsPointInPolygon(double lat, double lng, List<GeofenceCoordinateResponse> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var yi = (double)polygon[i].Latitude;
            var xi = (double)polygon[i].Longitude;
            var yj = (double)polygon[j].Latitude;
            var xj = (double)polygon[j].Longitude;

            var intersect = ((yi > lat) != (yj > lat)) &&
                            (lng < (xj - xi) * (lat - yi) / ((yj - yi) == 0 ? double.Epsilon : (yj - yi)) + xi);
            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
