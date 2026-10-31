using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionPreflightRepository(
    AppDbContext db)
    : IMissionPreflightRepository
{
    public async Task<List<(Guid Id, Guid CreatedBy)>>
        GetDueMissionsAsync(
            DateTime now,
            int limit,
            CancellationToken cancellationToken = default)
    {
        var horizon =
            now.AddMinutes(15);

        var stale =
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    now,
                    DateTimeKind.Utc))
            .AddMinutes(-1);

        var rows = await db.Missions
            .AsNoTracking()
            .Where(m =>
                m.Status == "Approved" &&
                m.StartTime <= horizon &&
                m.EndTime > now &&
                !db.MissionValidationRuns.Any(r =>
                    r.MissionId == m.Id &&
                    r.Stage == "Preflight" &&
                    r.CheckedAt > stale))
            .OrderBy(m =>
                db.MissionValidationRuns
                    .Where(r =>
                        r.MissionId == m.Id &&
                        r.Stage == "Preflight")
                    .Max(r =>
                        (DateTimeOffset?)r.CheckedAt)
                ?? DateTimeOffset.MinValue)
            .ThenBy(m => m.StartTime)
            .Select(m => new
            {
                m.Id,
                m.CreatedBy
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (x.Id, x.CreatedBy))
            .ToList();
    }
}