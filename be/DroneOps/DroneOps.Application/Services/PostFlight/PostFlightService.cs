using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.PostFlight;

/// <summary>
/// Service phụ trách toàn bộ luồng nghiệp vụ Phân tích sau bay & Lịch sử chuyến bay (MF3 / FE-08 / FE-10)
/// Đóng vai trò là "Reporting & Analytics Service" trong sơ đồ Swimlane 04.
/// </summary>
public class PostFlightService : IPostFlightService
{
    private readonly IPostFlightRepository _repository;

    public PostFlightService(IPostFlightRepository repository)
    {
        _repository = repository;
    }

    // =========================================================================
    // 1. TRUY VẤN LỊCH SỬ CHUYẾN BAY (SCR-13 / UC-18)
    // Nghiệp vụ: Phân quyền dữ liệu theo vai trò và hỗ trợ bộ lọc đa tiêu chí
    // =========================================================================
    public async Task<PagedResponse<FlightHistoryItemResponse>> GetFlightHistoryAsync(
        GetFlightHistoryRequest request,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        var (missions, totalCount) = await _repository.GetFlightHistoryAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.Status,
            request.UavId,
            request.FromDate,
            request.ToDate,
            pilotUserId,
            cancellationToken);

        var items = new List<FlightHistoryItemResponse>();

        foreach (var m in missions)
        {
            var report = await _repository.GetReportByMissionIdAsync(m.Id, cancellationToken);
            var incidents = await _repository.GetIncidentsByMissionIdAsync(m.Id, cancellationToken);

            double duration = 0;
            if (m.StartTime.HasValue && m.EndTime.HasValue)
            {
                duration = Math.Round((m.EndTime.Value - m.StartTime.Value).TotalMinutes, 2);
            }
            else if (report != null)
            {
                duration = report.TotalFlightDurationMinutes;
            }

            items.Add(new FlightHistoryItemResponse
            {
                MissionId = m.Id,
                MissionName = m.Name,
                Status = m.Status,
                UavId = m.UAVId,
                UavName = m.UAV?.Name ?? "N/A",
                UavCode = m.UAV?.Code ?? "N/A",
                PilotName = m.Creator?.FullName ?? "N/A",
                StartTime = m.StartTime,
                EndTime = m.EndTime,
                DurationMinutes = duration,
                TotalIncidents = incidents.Count,
                HasReport = report != null
            });
        }

        return new PagedResponse<FlightHistoryItemResponse>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }

    // =========================================================================
    // 2. CHỐT KẾT THÚC CHUYẾN BAY & TỰ ĐỘNG LẬP BÁO CÁO PHÂN TÍCH (UC-17)
    // Nghiệp vụ: Chuyển trạng thái sang Completed, quét dữ liệu viễn trắc (Telemetry)
    // và sự cố (Incidents) để tính toán hiệu suất bay, tiêu hao pin, quãng đường bay.
    // =========================================================================
    public async Task<PostFlightReportResponse> FinalizeAndGenerateReportAsync(
        Guid missionId,
        FinalizeFlightRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken);
        if (mission == null)
        {
            // Đã đổi sang tiếng Anh
            throw new Exception("Flight mission not found.");
        }

        if (mission.Status != "Completed")
        {
            mission.Status = "Completed";
            mission.EndTime ??= DateTime.UtcNow;
            await _repository.UpdateMissionAsync(mission, cancellationToken);
        }

        var telemetry = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        var incidents = await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);

        var report = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken);
        bool isNew = false;
        if (report == null)
        {
            report = new PostFlightReport
            {
                Id = Guid.NewGuid(),
                MissionId = missionId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            isNew = true;
        }

        double durationMinutes = 0;
        if (mission.StartTime.HasValue && mission.EndTime.HasValue)
        {
            durationMinutes = Math.Max(0, (mission.EndTime.Value - mission.StartTime.Value).TotalMinutes);
        }
        else if (telemetry.Count > 1)
        {
            durationMinutes = (telemetry.Last().RecordedAt - telemetry.First().RecordedAt).TotalMinutes;
        }
        report.TotalFlightDurationMinutes = Math.Round(durationMinutes, 2);

        if (telemetry.Any())
        {
            report.MaxAltitude = telemetry.Max(t => t.Altitude);
            report.AvgAltitude = Math.Round(telemetry.Where(t => t.Altitude.HasValue).Average(t => t.Altitude) ?? 0, 2);

            report.MaxSpeed = telemetry.Max(t => t.Speed);
            report.AvgSpeed = Math.Round(telemetry.Where(t => t.Speed.HasValue).Average(t => t.Speed) ?? 0, 2);

            report.InitialBatteryLevel = telemetry.First().BatteryLevel;
            report.FinalBatteryLevel = telemetry.Last().BatteryLevel;
            if (report.InitialBatteryLevel.HasValue && report.FinalBatteryLevel.HasValue)
            {
                report.BatteryConsumedPercentage = Math.Max(0, report.InitialBatteryLevel.Value - report.FinalBatteryLevel.Value);
            }

            double totalMeters = 0;
            for (int i = 0; i < telemetry.Count - 1; i++)
            {
                if (telemetry[i].Latitude.HasValue && telemetry[i].Longitude.HasValue &&
                    telemetry[i + 1].Latitude.HasValue && telemetry[i + 1].Longitude.HasValue)
                {
                    totalMeters += CalculateHaversineDistance(
                        telemetry[i].Latitude!.Value, telemetry[i].Longitude!.Value,
                        telemetry[i + 1].Latitude!.Value, telemetry[i + 1].Longitude!.Value);
                }
            }
            report.TotalDistanceMeters = (decimal)Math.Round(totalMeters, 2);
        }

        report.TotalTelemetryRecords = telemetry.Count;
        report.TotalIncidents = incidents.Count;

        // Đánh giá an toàn tự động (Rule-based Safety Assessment) - Đã chuẩn hóa tiếng Anh
        if (incidents.Any(i => i.Severity?.ToLower() == "critical" || i.Severity?.ToLower() == "high"))
        {
            report.SafetyEvaluation = "HIGH RISK: Critical or high-severity incidents detected during flight.";
        }
        else if (incidents.Any())
        {
            report.SafetyEvaluation = "MEDIUM RISK: Minor or medium-severity incidents reported.";
        }
        else if (report.FinalBatteryLevel.HasValue && report.FinalBatteryLevel.Value < 20)
        {
            report.SafetyEvaluation = "LOW BATTERY WARNING: Final battery level dropped below the safe 20% threshold.";
        }
        else
        {
            report.SafetyEvaluation = "SAFE: All operational telemetry parameters remained within normal limits with zero incidents.";
        }

        report.Summary = request?.Summary ?? $"Mission '{mission.Name}' completed with {report.TotalTelemetryRecords} telemetry samples and {report.TotalIncidents} recorded incidents.";
        report.Notes = request?.Notes;
        report.Status = "Completed";

        if (isNew)
        {
            await _repository.AddReportAsync(report, cancellationToken);
        }
        await _repository.SaveChangesAsync(cancellationToken);

        return MapToReportResponse(report, mission);
    }

    // =========================================================================
    // 3. XEM CHI TIẾT BÁO CÁO SAU BAY (SCR-14 / UC-18)
    // =========================================================================
    public async Task<PostFlightReportResponse> GetReportByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        var report = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken);
        if (report == null)
        {
            return await FinalizeAndGenerateReportAsync(missionId, null, cancellationToken);
        }

        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken);
        return MapToReportResponse(report, mission ?? report.Mission);
    }

    // =========================================================================
    // 4. LẤY DỮ LIỆU ĐƯỜNG BAY VIỄN TRẮC (TELEMETRY PLAYBACK)
    // =========================================================================
    public async Task<List<FlightTelemetryPointResponse>> GetTelemetryPlaybackAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        var records = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        return records.Select(r => new FlightTelemetryPointResponse
        {
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            Altitude = r.Altitude,
            Speed = r.Speed,
            BatteryLevel = r.BatteryLevel,
            RecordedAt = r.RecordedAt
        }).ToList();
    }

    // =========================================================================
    // 5. TRUY VẤN DANH SÁCH SỰ CỐ GẮN LIỀN VỚI CHUYẾN BAY (SCR-12 / UC-16)
    // =========================================================================
    public async Task<List<Incident>> GetIncidentsAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);
    }

    // =========================================================================
    // 6. TỔNG HỢP CHỈ SỐ THỐNG KÊ VẬN HÀNH CHO DASHBOARD (SCR-16 / UC-22)
    // =========================================================================
    public async Task<OperationalAnalyticsResponse> GetOperationalAnalyticsAsync(Guid? pilotUserId = null, CancellationToken cancellationToken = default)
    {
        var reports = await _repository.GetAllCompletedReportsAsync(pilotUserId, cancellationToken);

        int total = reports.Count;
        int completed = reports.Count(r => r.Status == "Completed");
        int failed = total - completed;

        double totalHours = Math.Round(reports.Sum(r => r.TotalFlightDurationMinutes) / 60.0, 2);
        decimal totalKm = Math.Round(reports.Sum(r => r.TotalDistanceMeters ?? 0) / 1000m, 2);
        int totalIncidents = reports.Sum(r => r.TotalIncidents);
        double successRate = total > 0 ? Math.Round((completed / (double)total) * 100, 1) : 100.0;

        return new OperationalAnalyticsResponse
        {
            TotalFlights = total,
            CompletedFlights = completed,
            AbortedOrFailedFlights = failed,
            TotalFlightHours = totalHours,
            TotalDistanceKilometers = totalKm,
            TotalIncidentsRecorded = totalIncidents,
            SuccessRatePercentage = successRate
        };
    }

    // =========================================================================
    // 7. THUẬT TOÁN HAVERSINE TÍNH KHOẢNG CÁCH GPS (Đơn vị: Mét)
    // =========================================================================
    private static double CalculateHaversineDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double R = 6371000;
        var dLat = (double)(lat2 - lat1) * Math.PI / 180.0;
        var dLon = (double)(lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos((double)lat1 * Math.PI / 180.0) * Math.Cos((double)lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    // =========================================================================
    // 8. HÀM ÁNH XẠ DỮ LIỆU ENTITY SANG DTO RESPONSE
    // =========================================================================
    private static PostFlightReportResponse MapToReportResponse(PostFlightReport report, Mission mission)
    {
        return new PostFlightReportResponse
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
}