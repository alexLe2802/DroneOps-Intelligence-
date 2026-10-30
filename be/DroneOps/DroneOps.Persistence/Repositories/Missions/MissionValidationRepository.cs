using System.Data;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionValidationRepository(
    AppDbContext db)
    : IMissionValidationRepository
{
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
        return await db.Geofences
            .AsNoTracking()
            .ToListAsync(cancellationToken);
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

    public async Task<bool> HasScheduleConflictAsync(
        Guid missionId,
        Guid? uavId,
        Guid createdBy,
        DateTime? startTime,
        DateTime? endTime,
        CancellationToken cancellationToken = default)
    {
        if (startTime is null || endTime is null)
        {
            return false;
        }

        return await db.Missions
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.Id != missionId &&
                    (x.UAVId == uavId ||
                     x.CreatedBy == createdBy) &&
                    (
                        x.Status == "Approved" ||
                        x.Status == "InProgress" ||
                        x.Status == "In Progress"
                    ) &&
                    x.StartTime < endTime &&
                    x.EndTime > startTime,
                cancellationToken);
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