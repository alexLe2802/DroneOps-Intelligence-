using System.Data;
using DroneOps.Domain.Constants;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionValidationRepository(
    AppDbContext db,
    IMemoryCache cache)
    : IMissionValidationRepository
{
    public Task<MissionUpdateRequest?> GetReviewRequestAsync(Guid missionId, Guid requestId, CancellationToken ct = default)
        => db.MissionUpdateRequests.SingleOrDefaultAsync(x => x.MissionId == missionId && x.Id == requestId, ct);
    public async Task<Mission?> GetMissionWithWaypointsAsync(
        Guid missionId,
        bool tracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Mission> query = db.Missions
            .Include(x => x.Waypoints);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            x => x.Id == missionId,
            cancellationToken);
    }

    public async Task<List<MissionReviewHistoryRecord>> GetReviewHistoryAsync(
        Guid missionId, CancellationToken cancellationToken = default)
    {
        return await (from request in db.MissionUpdateRequests.AsNoTracking()
            join run in db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == missionId)
                on request.ValidationRunId equals (Guid?)run.Id into runs
            from run in runs.DefaultIfEmpty()
            where request.MissionId == missionId
            orderby request.CreatedAt descending, request.Id descending
            select new MissionReviewHistoryRecord(request, run)).ToListAsync(cancellationToken);
    }

    public async Task<Mission?> GetMissionAsync(
        Guid missionId,
        bool tracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Mission> query = db.Missions;

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            x => x.Id == missionId,
            cancellationToken);
    }

    public async Task<MissionValidationRun?> GetLatestRunAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionValidationRuns
            .AsNoTracking()
            .Where(x => x.MissionId == missionId)
            .OrderByDescending(x => x.CheckedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MissionValidationRun?> GetLatestPreflightRunAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionValidationRuns
            .AsNoTracking()
            .Where(x =>
                x.MissionId == missionId &&
                x.Stage == "Preflight")
            .OrderByDescending(x => x.CheckedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<MissionValidationRun>> GetLatestRunsAsync(
        IReadOnlyCollection<Guid> missionIds,
        bool preflightOnly,
        CancellationToken cancellationToken = default)
    {
        if (missionIds.Count == 0)
        {
            return [];
        }

        var query = db.MissionValidationRuns
            .AsNoTracking()
            .Where(x => x.MissionId.HasValue && missionIds.Contains(x.MissionId.Value));

        if (preflightOnly)
        {
            query = query.Where(x => x.Stage == "Preflight");
        }

        return await query
            .GroupBy(x => x.MissionId)
            .Select(group => group
                .OrderByDescending(x => x.CheckedAt)
                .ThenByDescending(x => x.Id)
                .First())
            .ToListAsync(cancellationToken);
    }

    public async Task<MissionValidationRun?> GetRunAsync(
        Guid missionId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionValidationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == runId &&
                     x.MissionId == missionId,
                cancellationToken);
    }

    public async Task<MissionValidationRun?> GetRecentPreflightRunAsync(
        Guid missionId,
        DateTimeOffset checkedAfter,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionValidationRuns
            .AsNoTracking()
            .Where(x =>
                x.MissionId == missionId &&
                x.Stage == "Preflight" &&
                x.CheckedAt > checkedAfter)
            .OrderByDescending(x => x.CheckedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Geofence>> GetGeofencesAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(
                GeofenceCacheKeys.Snapshot,
                out IReadOnlyList<Geofence>? cached) && cached is not null)
        {
            return cached.ToList();
        }

        var snapshot = await db.Geofences
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        cache.Set(
            GeofenceCacheKeys.Snapshot,
            (IReadOnlyList<Geofence>)snapshot,
            GeofenceCacheKeys.SnapshotLifetime);
        return snapshot;
    }

    public async Task<List<(Guid Id, long Revision)>>
        GetGeofenceRevisionsAsync(
            CancellationToken cancellationToken = default)
    {
        var revisions = await db.Geofences
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Revision
            })
            .ToListAsync(cancellationToken);

        return revisions
            .Select(x => (x.Id, x.Revision))
            .ToList();
    }

    public async Task<Pilot?> GetPilotByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await db.Pilots
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken);
    }

    public async Task<UAV?> GetUavByIdAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        return await db.UAVs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == uavId,
                cancellationToken);
    }

    public async Task<List<MissionScheduleConflict>> GetScheduleConflictsAsync(
        Guid missionId,
        Guid? uavId,
        Guid createdBy,
        DateTime? startTime,
        DateTime? endTime,
        CancellationToken cancellationToken = default)
    {
        if (startTime is null || endTime is null)
            return [];

        return await db.Missions.AsNoTracking()
            .Where(x => x.Id != missionId
                && ((uavId.HasValue && x.UAVId == uavId) || x.CreatedBy == createdBy)
                && (x.Status == "Approved" || x.Status == "InProgress" || x.Status == "In Progress")
                && x.StartTime < endTime && x.EndTime > startTime)
            .OrderBy(x => x.StartTime).ThenBy(x => x.Id)
            .Select(x => new MissionScheduleConflict(x.Id, x.Name ?? "Unnamed mission", x.Status,
                x.StartTime!.Value, x.EndTime!.Value,
                uavId.HasValue && x.UAVId == uavId, x.CreatedBy == createdBy))
            .Take(5).ToListAsync(cancellationToken);
    }

    public async Task<Incident?> GetOpenPreflightIncidentAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await db.Incidents
            .FirstOrDefaultAsync(
                x =>
                    x.MissionId == missionId &&
                    x.Type == "PreflightValidation" &&
                    x.Status == "Open",
                cancellationToken);
    }

    public async Task AddValidationRunAsync(
        MissionValidationRun run,
        CancellationToken cancellationToken = default)
    {
        await db.MissionValidationRuns.AddAsync(
            run,
            cancellationToken);
    }

    public async Task AddIncidentAsync(
        Incident incident,
        CancellationToken cancellationToken = default)
    {
        await db.Incidents.AddAsync(
            incident,
            cancellationToken);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                var result = await operation(
                    cancellationToken);

                await db.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                throw;
            }
        });
    }
}
