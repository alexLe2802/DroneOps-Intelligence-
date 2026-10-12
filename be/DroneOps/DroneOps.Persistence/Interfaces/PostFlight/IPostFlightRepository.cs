using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IPostFlightRepository
{
    // Nhận các kiểu dữ liệu nguyên thủy chuẩn kiến trúc Persistence
    Task<(List<Mission> Items, int TotalCount)> GetFlightHistoryAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        string? status = null,
        Guid? uavId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default);

    Task<Mission?> GetMissionWithDetailsAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<List<TelemetryRecord>> GetTelemetryByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<List<Incident>> GetIncidentsByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<PostFlightReport?> GetReportByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task AddReportAsync(PostFlightReport report, CancellationToken cancellationToken = default);

    Task UpdateMissionAsync(Mission mission, CancellationToken cancellationToken = default);

    Task<List<PostFlightReport>> GetAllCompletedReportsAsync(Guid? pilotUserId = null, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}