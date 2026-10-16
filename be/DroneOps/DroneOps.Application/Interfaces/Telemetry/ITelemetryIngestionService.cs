using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;

namespace DroneOps.Application.Interfaces.Telemetry;

public interface ITelemetryIngestionService
{
    Task<TelemetryIngestResult> IngestSampleAsync(
        string idempotencyKey,
        IngestTelemetrySampleRequest request,
        CancellationToken cancellationToken = default);

    Task<MissionLatestTelemetryResponse?> GetLatestMissionTelemetryAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    Task<List<LiveMissionItemResponse>> GetLiveMissionsAsync(
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);
}