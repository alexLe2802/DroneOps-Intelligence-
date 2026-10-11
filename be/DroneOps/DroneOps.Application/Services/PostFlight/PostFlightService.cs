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
        // Bước 1: Gọi Repository lấy danh sách Mission thỏa mãn điều kiện lọc kèm tổng số bản ghi (TotalCount)
        // Nếu là tài khoản Pilot (pilotUserId != null), Repository chỉ lọc các chuyến bay do Pilot đó thực hiện.
        // Nếu là Operations Manager/Admin (pilotUserId == null), hệ thống trả về toàn bộ chuyến bay trong hệ thống.
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

        // Bước 2: Duyệt từng chuyến bay để bóc tách thông tin tóm tắt hiển thị lên bảng danh sách
        foreach (var m in missions)
        {
            var report = await _repository.GetReportByMissionIdAsync(m.Id, cancellationToken);
            var incidents = await _repository.GetIncidentsByMissionIdAsync(m.Id, cancellationToken);

            // Tính thời lượng bay (phút): Ưu tiên tính từ StartTime và EndTime thực tế của Mission
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
                TotalIncidents = incidents.Count, // Tổng số sự cố ghi nhận trong chuyến bay
                HasReport = report != null        // Cờ đánh dấu chuyến bay này đã lập báo cáo hay chưa
            });
        }

        // Bước 3: Đóng gói kết quả phân trang theo chuẩn PagedResponse
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
        // Bước 1: Kiểm tra sự tồn tại của Mission
        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken);
        if (mission == null)
        {
            throw new Exception("Không tìm thấy thông tin chuyến bay / nhiệm vụ.");
        }

        // Bước 2: Chốt trạng thái chuyến bay sang 'Completed' và gán mốc thời gian kết thúc (EndTime)
        if (mission.Status != "Completed")
        {
            mission.Status = "Completed";
            mission.EndTime ??= DateTime.UtcNow;
            await _repository.UpdateMissionAsync(mission, cancellationToken);
        }

        // Bước 3: Thu thập toàn bộ dữ liệu viễn trắc (Telemetry) và sự cố (Incidents) đã lưu trong suốt chuyến bay
        var telemetry = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        var incidents = await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);

        // Bước 4: Khởi tạo hoặc cập nhật bản ghi PostFlightReport
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

        // Bước 5: Tính tổng thời gian bay thực tế (phút)
        double durationMinutes = 0;
        if (mission.StartTime.HasValue && mission.EndTime.HasValue)
        {
            durationMinutes = Math.Max(0, (mission.EndTime.Value - mission.StartTime.Value).TotalMinutes);
        }
        else if (telemetry.Count > 1)
        {
            // Dự phòng: Nếu StartTime/EndTime bị thiếu, tính theo khoảng thời gian giữa bản ghi viễn trắc đầu và cuối
            durationMinutes = (telemetry.Last().RecordedAt - telemetry.First().RecordedAt).TotalMinutes;
        }
        report.TotalFlightDurationMinutes = Math.Round(durationMinutes, 2);

        // Bước 6: Phân tích các chỉ số viễn trắc chi tiết (Độ cao, Tốc độ, Mức pin, Quãng đường)
        if (telemetry.Any())
        {
            // 6.1. Độ cao tối đa và Độ cao trung bình
            report.MaxAltitude = telemetry.Max(t => t.Altitude);
            report.AvgAltitude = Math.Round(telemetry.Where(t => t.Altitude.HasValue).Average(t => t.Altitude) ?? 0, 2);

            // 6.2. Vận tốc tối đa và Vận tốc trung bình
            report.MaxSpeed = telemetry.Max(t => t.Speed);
            report.AvgSpeed = Math.Round(telemetry.Where(t => t.Speed.HasValue).Average(t => t.Speed) ?? 0, 2);

            // 6.3. Tiêu hao năng lượng (% pin bắt đầu - % pin kết thúc)
            report.InitialBatteryLevel = telemetry.First().BatteryLevel;
            report.FinalBatteryLevel = telemetry.Last().BatteryLevel;
            if (report.InitialBatteryLevel.HasValue && report.FinalBatteryLevel.HasValue)
            {
                report.BatteryConsumedPercentage = Math.Max(0, report.InitialBatteryLevel.Value - report.FinalBatteryLevel.Value);
            }

            // 6.4. Tính toán quãng đường bay tích lũy thực tế bằng công thức Haversine qua các cặp tọa độ GPS liên tiếp
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

        // Bước 7: Đánh giá an toàn tự động (Rule-based Safety Assessment) dựa trên mức độ nghiêm trọng của sự cố và pin
        if (incidents.Any(i => i.Severity?.ToLower() == "critical" || i.Severity?.ToLower() == "high"))
        {
            report.SafetyEvaluation = "Mức độ rủi ro CAO: Đã phát hiện sự cố nghiêm trọng (Critical/High) trong hành trình bay.";
        }
        else if (incidents.Any())
        {
            report.SafetyEvaluation = "Mức độ rủi ro TRUNG BÌNH: Phát sinh cảnh báo sự cố mức độ nhẹ hoặc trung bình.";
        }
        else if (report.FinalBatteryLevel.HasValue && report.FinalBatteryLevel.Value < 20)
        {
            report.SafetyEvaluation = "Cảnh báo pin yếu: Mức pin khi hạ cánh dưới 20% ngưỡng an toàn.";
        }
        else
        {
            report.SafetyEvaluation = "Chuyến bay AN TOÀN: Toàn bộ thông số vận hành ổn định, không ghi nhận sự cố.";
        }

        report.Summary = request?.Summary ?? $"Chuyến bay '{mission.Name}' kết thúc với {report.TotalTelemetryRecords} bản ghi viễn trắc và {report.TotalIncidents} sự cố.";
        report.Notes = request?.Notes;
        report.Status = "Completed";

        // Bước 8: Lưu dữ liệu báo cáo vào Database
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
            // Cơ chế tự phục hồi (Fallback): Nếu chuyến bay đã hoàn thành nhưng chưa có báo cáo, tự động tính toán và tạo ngay
            return await FinalizeAndGenerateReportAsync(missionId, null, cancellationToken);
        }

        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken);
        return MapToReportResponse(report, mission ?? report.Mission);
    }

    // =========================================================================
    // 4. LẤY DỮ LIỆU ĐƯỜNG BAY VIỄN TRẮC (TELEMETRY PLAYBACK)
    // Nghiệp vụ: Phục vụ Frontend vẽ lại toàn bộ vệt bay (Flight Trajectory Path) trên bản đồ
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
    // Nghiệp vụ: Tính tổng số chuyến bay, số giờ bay tích lũy, quãng đường km và tỷ lệ bay thành công
    // =========================================================================
    public async Task<OperationalAnalyticsResponse> GetOperationalAnalyticsAsync(Guid? pilotUserId = null, CancellationToken cancellationToken = default)
    {
        var reports = await _repository.GetAllCompletedReportsAsync(pilotUserId, cancellationToken);

        int total = reports.Count;
        int completed = reports.Count(r => r.Status == "Completed");
        int failed = total - completed;

        // Tổng thời gian bay quy đổi sang giờ (Hours)
        double totalHours = Math.Round(reports.Sum(r => r.TotalFlightDurationMinutes) / 60.0, 2);

        // Tổng quãng đường bay quy đổi sang kilômét (Km)
        decimal totalKm = Math.Round(reports.Sum(r => r.TotalDistanceMeters ?? 0) / 1000m, 2);

        int totalIncidents = reports.Sum(r => r.TotalIncidents);

        // Tỷ lệ hoàn thành nhiệm vụ thành công (%)
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
    // 7. THUẬT TOÁN HAVERSINE TÍNH KHOẢNG CÁCH CUNG TRÒN GIỮA 2 TỌA ĐỘ GPS
    // Công thức: d = 2R * arcsin(sqrt(sin^2(dLat/2) + cos(lat1)*cos(lat2)*sin^2(dLon/2)))
    // Đơn vị tính: Mét
    // =========================================================================
    private static double CalculateHaversineDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double R = 6371000; // Bán kính trung bình của Trái Đất (mét)
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