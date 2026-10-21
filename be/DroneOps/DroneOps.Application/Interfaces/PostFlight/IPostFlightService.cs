using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.PostFlight;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.PostFlight;

public interface IPostFlightService
{
    // Lấy danh sách lịch sử chuyến bay kèm phân trang (SCR-13 / UC-18)
    Task<PagedResponse<FlightHistoryItemResponse>> GetFlightHistoryAsync(
        GetFlightHistoryRequest request,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default);

    // Chốt hoàn tất chuyến bay & Tự động tính toán sinh PostFlightReport (UC-17)
    Task<PostFlightReportResponse> FinalizeAndGenerateReportAsync(
        Guid missionId,
        FinalizeFlightRequest? request = null,
        Guid? currentUserId = null,
        bool isManager = false,
        CancellationToken cancellationToken = default);

    // Được background worker gọi để tự động chốt chuyến bay khi phát hiện cạnh Armed true -> false.
    Task<bool> TryAutoFinalizeAsync(Guid missionId, CancellationToken cancellationToken = default);

    // Xem chi tiết báo cáo sau bay (SCR-14 / UC-18)
    Task<PostFlightReportResponse> GetReportByMissionIdAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    // Lấy chuỗi tọa độ viễn trắc để vẽ lại lộ trình bay thực tế
    Task<List<FlightTelemetryPointResponse>> GetTelemetryPlaybackAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    // Lấy danh sách sự cố trong chuyến bay (SCR-12 / UC-16)
    Task<List<Incident>> GetIncidentsAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    // Thống kê phân tích vận hành (SCR-16 / UC-22)
    Task<OperationalAnalyticsResponse> GetOperationalAnalyticsAsync(Guid? pilotUserId = null, CancellationToken cancellationToken = default);
}
