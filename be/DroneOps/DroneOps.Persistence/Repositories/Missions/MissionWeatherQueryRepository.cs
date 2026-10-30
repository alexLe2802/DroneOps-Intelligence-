using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionWeatherQueryRepository(
    AppDbContext db)
    : IMissionWeatherQueryRepository
{
    public async Task<Mission?> GetMissionWithWaypointsAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await db.Missions
            .AsNoTracking()
            .Include(x => x.Waypoints)
            .SingleOrDefaultAsync(
                x => x.Id == missionId,
                cancellationToken);
    }

    public async Task<MissionWeatherCache?> GetCacheAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionWeatherCaches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Key == key,
                cancellationToken);
    }

    public async Task UpsertCacheAsync(
        string key,
        string forecastJson,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "MissionWeatherCache"
                ("Key", "ForecastJson", "FetchedAt", "ExpiresAt")
            VALUES
                ({key}, {forecastJson}::jsonb, {fetchedAt}, {expiresAt})
            ON CONFLICT ("Key")
            DO UPDATE SET
                "ForecastJson" = EXCLUDED."ForecastJson",
                "FetchedAt" = EXCLUDED."FetchedAt",
                "ExpiresAt" = EXCLUDED."ExpiresAt"
            """,
            cancellationToken);
    }
}