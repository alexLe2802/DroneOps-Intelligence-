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

    Task<FlightCompletionEvent?> GetCompletionEventByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task<FlightCompletionEvent?> GetTrustedCompletionEventAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task AddCompletionEventAsync(FlightCompletionEvent completionEvent, CancellationToken cancellationToken = default);

    Task<List<Incident>> GetIncidentsByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<PostFlightReport?> GetReportByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<List<PostFlightReport>> GetReportRevisionsAsync(Guid missionId, CancellationToken cancellationToken = default);

    Task<List<PostFlightReport>> GetReportsAwaitingGenerationAsync(int batchSize, CancellationToken cancellationToken = default);

    Task AddReportAsync(PostFlightReport report, CancellationToken cancellationToken = default);

    Task ReplaceCurrentReportAsync(
        PostFlightReport currentReport,
        PostFlightReport nextReport,
        CancellationToken cancellationToken = default);

    Task UpdateMissionAsync(Mission mission, CancellationToken cancellationToken = default);

    Task<List<Mission>> GetTerminalMissionsAsync(
        DateTime? fromDate, DateTime? toDate, Guid? uavId, Guid? missionId,
        Guid? pilotUserId, CancellationToken cancellationToken = default);

    Task MarkReportsOutdatedForLateTelemetryAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
