using System.Text.Json;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Geofences;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.Telemetry;

public class TelemetryIngestionService : ITelemetryIngestionService
{
    private const int OnlineWindowSeconds = 10;
    private const int StaleWindowSeconds = 30;
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

    public TelemetryIngestionService(
        AppDbContext context,
        ILogger<TelemetryIngestionService> logger,
        IOptions<TelemetryMonitoringOptions> monitoring)
    {
        _context = context;
        _logger = logger;
        _monitoring = monitoring.Value;
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
                _logger.LogInformation("Duplicate telemetry packet ignored. Key hash length: {Length}", idempotencyKey.Length);
                return new TelemetryIngestResult
                {
                    Accepted = true,
                    IsDuplicate = true,
                    RecordId = existing.Id,
                    UsedForLatestProjection = existing.IsValid,
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
        var clockSkew = request.SourceTimestamp - now;
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
        var isLateArrival = currentLatestSource.HasValue && request.SourceTimestamp < currentLatestSource.Value;
        var usedForLatest = !isFutureSkew && !isLateArrival;

        var record = new TelemetryRecord
        {
            Id = Guid.NewGuid(),
            SourceId = request.SourceId,
            UAVId = request.UavId,
            MissionId = missionId,
            SourceTimestamp = request.SourceTimestamp,
            ReceivedAt = now,
            RecordedAt = request.SourceTimestamp,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Altitude = request.AltitudeMeters,
            AltitudeReference = string.IsNullOrWhiteSpace(request.AltitudeReference)
                ? "Relative"
                : request.AltitudeReference,
            Speed = request.GroundSpeedMetersPerSecond,
            BatteryLevel = request.BatteryPercent,
            FlightMode = string.IsNullOrWhiteSpace(request.FlightMode) ? null : request.FlightMode.Trim(),
            Armed = request.Armed,
            MissionSequence = request.MissionSequence,
            IdempotencyKey = idempotencyKey,
            IsValid = !isFutureSkew
        };

        await _context.TelemetryRecords.AddAsync(record, cancellationToken);

        Guid? raisedIncidentId = null;
        if (usedForLatest && record.IsValid && missionId.HasValue)
        {
            raisedIncidentId = await TryRaiseOperationalIncidentsAsync(record, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new TelemetryIngestResult
        {
            Accepted = true,
            IsDuplicate = false,
            IsLateArrival = isLateArrival,
            UsedForLatestProjection = usedForLatest && record.IsValid,
            RecordId = record.Id,
            RaisedIncidentId = raisedIncidentId,
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
        response.BatteryPercent = latestRecord.BatteryLevel;
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
                item.BatteryPercent = latest.BatteryLevel;
                item.FlightMode = latest.FlightMode;
                item.Armed = latest.Armed;
                item.MissionSequence = latest.MissionSequence;
                item.ProgressPercent = ComputeProgress(latest.MissionSequence, mission.WaypointTotal);
            }

            result.Add(item);
        }

        return result;
    }

    private async Task<Guid?> TryRaiseOperationalIncidentsAsync(
        TelemetryRecord record,
        CancellationToken cancellationToken)
    {
        if (!record.MissionId.HasValue)
        {
            return null;
        }

        var missionId = record.MissionId.Value;
        Guid? firstRaisedIncidentId = null;

        if (record.BatteryLevel.HasValue && record.BatteryLevel.Value <= _monitoring.LowBatteryPercent)
        {
            var raised = await RaiseIfOpenMissingAsync(
                missionId,
                type: "LowBattery",
                title: "Low battery detected",
                severity: record.BatteryLevel.Value <= 10 ? "Critical" : "High",
                description: $"Battery dropped to {record.BatteryLevel.Value}%.",
                record,
                appliedRule: "LowBattery",
                threshold: _monitoring.LowBatteryPercent,
                cancellationToken);

            if (raised.HasValue)
            {
                firstRaisedIncidentId ??= raised;
            }
        }

        if (record.Altitude.HasValue && record.Altitude.Value > _monitoring.MaximumAltitudeMeters)
        {
            var raised = await RaiseIfOpenMissingAsync(
                missionId,
                type: "AltitudeExceeded",
                title: "Altitude limit exceeded",
                severity: "High",
                description: $"Altitude reached {record.Altitude.Value} m.",
                record,
                appliedRule: "MaximumAltitude",
                threshold: (double)_monitoring.MaximumAltitudeMeters,
                cancellationToken);

            if (raised.HasValue)
            {
                firstRaisedIncidentId ??= raised;
            }
        }

        if (record.Speed.HasValue && record.Speed.Value > _monitoring.MaximumGroundSpeedMetersPerSecond)
        {
            var raised = await RaiseIfOpenMissingAsync(
                missionId,
                type: "SpeedExceeded",
                title: "Ground speed limit exceeded",
                severity: "High",
                description: $"Ground speed reached {record.Speed.Value} m/s.",
                record,
                appliedRule: "MaximumGroundSpeed",
                threshold: (double)_monitoring.MaximumGroundSpeedMetersPerSecond,
                cancellationToken);

            if (raised.HasValue)
            {
                firstRaisedIncidentId ??= raised;
            }
        }

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

                if (!insideAny)
                {
                    var raised = await RaiseIfOpenMissingAsync(
                        missionId,
                        type: "GeofenceBreach",
                        title: "Geofence breach detected",
                        severity: "High",
                        description: "UAV is outside every configured operating geofence.",
                        record,
                        appliedRule: "GeofenceKeepIn",
                        threshold: null,
                        cancellationToken);

                    if (raised.HasValue)
                    {
                        firstRaisedIncidentId ??= raised;
                    }
                }
            }
        }

        return firstRaisedIncidentId;
    }

    private async Task<Guid?> RaiseIfOpenMissingAsync(
        Guid missionId,
        string type,
        string title,
        string severity,
        string description,
        TelemetryRecord record,
        string appliedRule,
        double? threshold,
        CancellationToken cancellationToken)
    {
        var hasOpen = await _context.Incidents
            .AnyAsync(
                i => i.MissionId == missionId &&
                     i.Type == type &&
                     i.Status != "Resolved",
                cancellationToken);

        if (hasOpen)
        {
            return null;
        }

        var evidence = JsonSerializer.Serialize(new
        {
            sourceId = record.SourceId,
            latitude = record.Latitude,
            longitude = record.Longitude,
            altitude = record.Altitude,
            speed = record.Speed,
            batteryPercent = record.BatteryLevel,
            flightMode = record.FlightMode,
            sourceTimestamp = record.SourceTimestamp,
            receivedAt = record.ReceivedAt,
            appliedRule,
            threshold
        }, JsonOptions);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            MissionId = missionId,
            Title = title,
            Description = description,
            Type = type,
            Severity = severity,
            Status = "Open",
            Latitude = record.Latitude,
            Longitude = record.Longitude,
            Evidence = evidence,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _context.Incidents.AddAsync(incident, cancellationToken);
        return incident.Id;
    }

    private static void ApplyFreshness(MissionLatestTelemetryResponse response, DateTimeOffset receivedAt)
    {
        var ageSeconds = (DateTimeOffset.UtcNow - receivedAt).TotalSeconds;
        response.DataAgeSeconds = Math.Round(ageSeconds, 1);
        response.ConnectivityStatus = ClassifyConnectivity(ageSeconds);
        response.IsStale = ageSeconds > OnlineWindowSeconds;
        response.SnapshotKind = response.ConnectivityStatus switch
        {
            "Online" => "Latest",
            "Stale" => "Stale",
            _ => "Offline"
        };
    }

    private static string ClassifyConnectivity(double ageSeconds) => ageSeconds switch
    {
        <= OnlineWindowSeconds => "Online",
        <= StaleWindowSeconds => "Stale",
        _ => "Offline"
    };

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
