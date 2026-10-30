using System.Data;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionApprovalRepository(
    AppDbContext db)
    : IMissionApprovalRepository
{
    public async Task<(List<Mission> Missions, int TotalCount)>
        GetPendingApprovalsAsync(
            string? search,
            Guid? uavId,
            DateTime? fromDate,
            DateTime? toDate,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        var query = db.Missions
            .AsNoTracking()
            .Include(x => x.Creator)
            .Include(x => x.UAV)
            .Include(x => x.Waypoints)
            .Where(x => x.Status == "Pending Review");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchValue = search.Trim();

            query = query.Where(x =>
                x.Name.Contains(searchValue) ||
                (
                    x.Creator != null &&
                    x.Creator.FullName.Contains(searchValue)
                ));
        }

        if (uavId.HasValue)
        {
            query = query.Where(x =>
                x.UAVId == uavId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(x =>
                x.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x =>
                x.CreatedAt <= toDate.Value);
        }

        var totalCount =
            await query.CountAsync(cancellationToken);

        var missions = await query
            .OrderBy(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (missions, totalCount);
    }

    public async Task<Mission?> GetByIdWithDetailsAsync(
        Guid missionId,
        bool tracking = true,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Mission> query = db.Missions
            .Include(x => x.Waypoints)
            .Include(x => x.Creator)
            .Include(x => x.UAV);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            x => x.Id == missionId,
            cancellationToken);
    }

    public async Task<MissionApproval?> GetLatestDecisionAsync(
        Guid missionId,
        Guid managerId,
        string decision,
        CancellationToken cancellationToken = default)
    {
        return await db.MissionApprovals
            .AsNoTracking()
            .Where(x =>
                x.MissionId == missionId &&
                x.UserId == managerId &&
                x.Status == decision)
            .OrderByDescending(x => x.ApprovedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddApprovalAsync(
        MissionApproval approval,
        CancellationToken cancellationToken = default)
    {
        await db.MissionApprovals.AddAsync(
            approval,
            cancellationToken);
    }

    public async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var executionStrategy =
            db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                try
                {
                    var result =
                        await operation(cancellationToken);

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