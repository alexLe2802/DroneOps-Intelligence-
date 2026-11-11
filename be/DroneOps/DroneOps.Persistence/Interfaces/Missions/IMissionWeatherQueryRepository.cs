using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IMissionWeatherQueryRepository
{
    Task<MissionUpdateRequest?> GetReviewRequestAsync(Guid id, Guid requestId, CancellationToken ct);
    Task<Mission?> GetMissionWithWaypointsAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task<MissionWeatherCache?> GetCacheAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task UpsertCacheAsync(
        string key,
        string forecastJson,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}