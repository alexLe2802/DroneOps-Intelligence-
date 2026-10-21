using System.Collections.Concurrent;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.PostFlight;

/// <summary>
/// Thực hiện toàn bộ Flow 04: nhận biết kết thúc chuyến bay, hợp nhất dữ liệu,
/// tạo báo cáo và cung cấp lịch sử/analytics cho người vận hành.
/// </summary>
public sealed class PostFlightService : IPostFlightService
{
    private static readonly string[] FinalizableStatuses =
        ["In Progress", "InProgress", "Active", "Flying", "Executing"];
    private static readonly string[] AutoFinalizableStatuses =
        ["Approved", "In Progress", "InProgress", "Active", "Flying", "Executing"];

    // Khóa theo mission ngăn hai request trong cùng API instance cùng tạo hai report.
    // Unique index ở database vẫn là lớp bảo vệ cuối cùng khi chạy nhiều API instance.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> FinalizationLocks = new();
    private readonly IPostFlightRepository _repository;

    public PostFlightService(IPostFlightRepository repository) => _repository = repository;

    public async Task<PagedResponse<FlightHistoryItemResponse>> GetFlightHistoryAsync(
        GetFlightHistoryRequest request,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        // Chuẩn hóa phân trang tại service để repository không nhận Skip âm hoặc page quá lớn.
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (missions, totalCount) = await _repository.GetFlightHistoryAsync(
            pageNumber, pageSize, request.Search, request.Status, request.UavId,
            request.FromDate, request.ToDate, pilotUserId, cancellationToken);

        var items = new List<FlightHistoryItemResponse>(missions.Count);
        foreach (var mission in missions)
        {
            var report = await _repository.GetReportByMissionIdAsync(mission.Id, cancellationToken);
            var incidents = await _repository.GetIncidentsByMissionIdAsync(mission.Id, cancellationToken);
            items.Add(new FlightHistoryItemResponse
            {
                MissionId = mission.Id,
                MissionName = mission.Name,
                Status = mission.Status,
                UavId = mission.UAVId,
                UavName = mission.UAV?.Name ?? "N/A",
                UavCode = mission.UAV?.Code ?? "N/A",
                PilotName = mission.Creator?.FullName ?? "N/A",
                StartTime = mission.StartTime,
                EndTime = mission.EndTime,
                DurationMinutes = report?.TotalFlightDurationMinutes ?? CalculateScheduledDuration(mission),
                TotalIncidents = incidents.Count,
                HasReport = report != null
            });
        }

        return new PagedResponse<FlightHistoryItemResponse>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<PostFlightReportResponse> FinalizeAndGenerateReportAsync(
        Guid missionId,
        FinalizeFlightRequest? request = null,
        Guid? currentUserId = null,
        bool isManager = false,
        CancellationToken cancellationToken = default)
    {
        var gate = FinalizationLocks.GetOrAdd(missionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await FinalizeCoreAsync(
                missionId, request, currentUserId, isManager, isAutomatic: false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> TryAutoFinalizeAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        var gate = FinalizationLocks.GetOrAdd(missionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken);
            if (mission == null || mission.Status == "Completed" ||
                !AutoFinalizableStatuses.Contains(mission.Status, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var telemetry = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
            if (!HasArmedToDisarmedTransition(telemetry))
            {
                return false;
            }

            await FinalizeCoreAsync(
                missionId, request: null, currentUserId: null, isManager: false,
                isAutomatic: true, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PostFlightReportResponse> FinalizeCoreAsync(
        Guid missionId,
        FinalizeFlightRequest? request,
        Guid? currentUserId,
        bool isManager,
        bool isAutomatic,
        CancellationToken cancellationToken)
    {
        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Flight mission not found.");

        if (!isAutomatic)
        {
            EnsureMissionAccess(mission, currentUserId, isManager);
        }

        var existingReport = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken);

        // Finalize là idempotent: gọi lại trả report cũ, không tạo bản ghi thứ hai.
        if (mission.Status == "Completed" && existingReport != null)
        {
            return MapToReportResponse(existingReport, mission);
        }

        var allowedStatuses = isAutomatic ? AutoFinalizableStatuses : FinalizableStatuses;
        if (!allowedStatuses.Contains(mission.Status, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Mission with status '{mission.Status}' cannot be finalized. The mission must be actively flying.");
        }

        var telemetry = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        if (telemetry.Count == 0)
        {
            throw new InvalidOperationException("A mission cannot be finalized without valid telemetry records.");
        }
        if (isAutomatic && !HasArmedToDisarmedTransition(telemetry))
        {
            throw new InvalidOperationException("Automatic finalization requires an armed-to-disarmed transition.");
        }

        var incidents = await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);
        var report = existingReport ?? new PostFlightReport
        {
            Id = Guid.NewGuid(),
            MissionId = missionId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // SourceTimestamp phản ánh thời điểm drone phát dữ liệu, chính xác hơn lịch dự kiến.
        mission.Status = "Completed";
        mission.EndTime = telemetry[^1].SourceTimestamp.UtcDateTime;
        PopulateReport(report, mission, telemetry, incidents, request);

        if (existingReport == null)
        {
            await _repository.AddReportAsync(report, cancellationToken);
        }
        await _repository.UpdateMissionAsync(mission, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return MapToReportResponse(report, mission);
    }

    public async Task<PostFlightReportResponse> GetReportByMissionIdAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var mission = await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var report = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Post-flight report not found. Finalize the mission first.");

        // GET chỉ đọc dữ liệu và tuyệt đối không được tự thay đổi trạng thái mission.
        return MapToReportResponse(report, mission);
    }

    public async Task<List<FlightTelemetryPointResponse>> GetTelemetryPlaybackAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var records = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        return records.Select(record => new FlightTelemetryPointResponse
        {
            Latitude = record.Latitude,
            Longitude = record.Longitude,
            Altitude = record.Altitude,
            Speed = record.Speed,
            BatteryLevel = record.BatteryLevel,
            RecordedAt = record.SourceTimestamp
        }).ToList();
    }

    public async Task<List<Incident>> GetIncidentsAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        return await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);
    }

    public async Task<OperationalAnalyticsResponse> GetOperationalAnalyticsAsync(
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        var reports = await _repository.GetAllCompletedReportsAsync(pilotUserId, cancellationToken);
        var total = reports.Count;
        var completed = reports.Count(report => report.Status == "Completed");
        return new OperationalAnalyticsResponse
        {
            TotalFlights = total,
            CompletedFlights = completed,
            AbortedOrFailedFlights = total - completed,
            TotalFlightHours = Math.Round(reports.Sum(r => r.TotalFlightDurationMinutes) / 60.0, 2),
            TotalDistanceKilometers = Math.Round(reports.Sum(r => r.TotalDistanceMeters ?? 0) / 1000m, 2),
            TotalIncidentsRecorded = reports.Sum(r => r.TotalIncidents),
            SuccessRatePercentage = total == 0 ? 100 : Math.Round(completed * 100.0 / total, 1)
        };
    }

    private async Task<Mission> GetAuthorizedMissionAsync(
        Guid missionId, Guid? currentUserId, bool isManager, CancellationToken cancellationToken)
    {
        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Flight mission not found.");
        EnsureMissionAccess(mission, currentUserId, isManager);
        return mission;
    }

    private static void EnsureMissionAccess(Mission mission, Guid? currentUserId, bool isManager)
    {
        if (!isManager && (!currentUserId.HasValue || mission.CreatedBy != currentUserId.Value))
        {
            throw new UnauthorizedAccessException("You do not have permission to access this mission history.");
        }
    }

    private static bool HasArmedToDisarmedTransition(IReadOnlyList<TelemetryRecord> telemetry)
    {
        if (telemetry.Count < 2 || telemetry[^1].Armed != false)
        {
            return false;
        }
        return telemetry.Take(telemetry.Count - 1).Any(record => record.Armed == true);
    }

    private static void PopulateReport(
        PostFlightReport report,
        Mission mission,
        IReadOnlyList<TelemetryRecord> telemetry,
        IReadOnlyList<Incident> incidents,
        FinalizeFlightRequest? request)
    {
        var firstAt = telemetry[0].SourceTimestamp;
        var lastAt = telemetry[^1].SourceTimestamp;
        report.TotalFlightDurationMinutes = Math.Round(Math.Max(0, (lastAt - firstAt).TotalMinutes), 2);

        var altitudes = telemetry.Where(t => t.Altitude.HasValue).Select(t => t.Altitude!.Value).ToList();
        report.MaxAltitude = altitudes.Count == 0 ? null : altitudes.Max();
        report.AvgAltitude = altitudes.Count == 0 ? null : Math.Round(altitudes.Average(), 2);

        var speeds = telemetry.Where(t => t.Speed.HasValue).Select(t => t.Speed!.Value).ToList();
        report.MaxSpeed = speeds.Count == 0 ? null : speeds.Max();
        report.AvgSpeed = speeds.Count == 0 ? null : Math.Round(speeds.Average(), 2);

        var batteries = telemetry.Where(t => t.BatteryLevel.HasValue).Select(t => t.BatteryLevel!.Value).ToList();
        report.InitialBatteryLevel = batteries.Count == 0 ? null : batteries[0];
        report.FinalBatteryLevel = batteries.Count == 0 ? null : batteries[^1];
        report.BatteryConsumedPercentage = report.InitialBatteryLevel.HasValue && report.FinalBatteryLevel.HasValue
            ? Math.Max(0, report.InitialBatteryLevel.Value - report.FinalBatteryLevel.Value)
            : null;

        decimal totalDistanceMeters = 0;
        TelemetryRecord? previousGps = null;
        foreach (var current in telemetry.Where(t => t.Latitude.HasValue && t.Longitude.HasValue))
        {
            if (previousGps != null)
            {
                totalDistanceMeters += (decimal)CalculateHaversineDistance(
                    previousGps.Latitude!.Value, previousGps.Longitude!.Value,
                    current.Latitude!.Value, current.Longitude!.Value);
            }
            previousGps = current;
        }

        report.TotalDistanceMeters = Math.Round(totalDistanceMeters, 2);
        report.TotalTelemetryRecords = telemetry.Count;
        report.TotalIncidents = incidents.Count;
        report.SafetyEvaluation = BuildSafetyEvaluation(incidents, report.FinalBatteryLevel);
        report.Summary = string.IsNullOrWhiteSpace(request?.Summary)
            ? $"Mission '{mission.Name}' completed with {telemetry.Count} telemetry samples and {incidents.Count} recorded incidents."
            : request.Summary.Trim();
        report.Notes = string.IsNullOrWhiteSpace(request?.Notes) ? null : request.Notes.Trim();
        report.Status = "Completed";
    }

    private static string BuildSafetyEvaluation(IReadOnlyList<Incident> incidents, int? finalBattery)
    {
        if (incidents.Any(i => string.Equals(i.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(i.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            return "HIGH RISK: Critical or high-severity incidents were detected during flight.";
        if (incidents.Count > 0)
            return "MEDIUM RISK: Minor or medium-severity incidents were recorded during flight.";
        if (finalBattery is < 20)
            return "LOW BATTERY WARNING: Final battery level was below the safe 20% threshold.";
        return "SAFE: Telemetry remained within configured limits and no incidents were recorded.";
    }

    private static double CalculateScheduledDuration(Mission mission) =>
        mission.StartTime.HasValue && mission.EndTime.HasValue
            ? Math.Round(Math.Max(0, (mission.EndTime.Value - mission.StartTime.Value).TotalMinutes), 2)
            : 0;

    private static double CalculateHaversineDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double earthRadiusMeters = 6_371_000;
        var deltaLat = (double)(lat2 - lat1) * Math.PI / 180;
        var deltaLon = (double)(lon2 - lon1) * Math.PI / 180;
        var firstLat = (double)lat1 * Math.PI / 180;
        var secondLat = (double)lat2 * Math.PI / 180;
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(firstLat) * Math.Cos(secondLat) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static PostFlightReportResponse MapToReportResponse(PostFlightReport report, Mission mission) => new()
    {
        ReportId = report.Id,
        MissionId = mission.Id,
        MissionName = mission.Name,
        MissionDescription = mission.Description,
        Status = mission.Status,
        UavName = mission.UAV?.Name ?? "N/A",
        UavCode = mission.UAV?.Code ?? "N/A",
        PilotName = mission.Creator?.FullName ?? "N/A",
        StartTime = mission.StartTime,
        EndTime = mission.EndTime,
        TotalFlightDurationMinutes = report.TotalFlightDurationMinutes,
        MaxAltitude = report.MaxAltitude,
        AvgAltitude = report.AvgAltitude,
        MaxSpeed = report.MaxSpeed,
        AvgSpeed = report.AvgSpeed,
        InitialBatteryLevel = report.InitialBatteryLevel,
        FinalBatteryLevel = report.FinalBatteryLevel,
        BatteryConsumedPercentage = report.BatteryConsumedPercentage,
        TotalDistanceMeters = report.TotalDistanceMeters,
        TotalTelemetryRecords = report.TotalTelemetryRecords,
        TotalIncidents = report.TotalIncidents,
        SafetyEvaluation = report.SafetyEvaluation,
        Summary = report.Summary,
        Notes = report.Notes,
        CreatedAt = report.CreatedAt
    };
}
