using System.Data;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionWorkflowRepository(
    AppDbContext db) : IMissionWorkflowRepository
{
    public async Task<Mission?> GetByIdWithWaypointsAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await db.Missions
            .Include(x => x.Waypoints)
            .SingleOrDefaultAsync(
                x => x.Id == missionId,
                cancellationToken);
    }

    public async Task AddAsync(
        Mission mission,
        CancellationToken cancellationToken = default)
    {
        await db.Missions.AddAsync(
            mission,
            cancellationToken);
    }

    public async Task UpdatePlanAsync(
        Mission current,
        Mission proposal,
        CancellationToken cancellationToken = default)
    {
        /*
         * Current đang được EF Core tracking.
         * Copy các field của proposal vào current nhưng giữ nguyên
         * identity và thông tin người tạo.
         */

        var currentId = current.Id;
        var createdBy = current.CreatedBy;
        var createdAt = current.CreatedAt;

        db.Entry(current)
            .CurrentValues
            .SetValues(proposal);

        current.Id = currentId;
        current.CreatedBy = createdBy;
        current.CreatedAt = createdAt;
        current.Status = proposal.Status;

        /*
         * Thay toàn bộ waypoint cũ bằng waypoint mới.
         */
        db.Set<Waypoint>().RemoveRange(current.Waypoints);

        current.Waypoints.Clear();

        foreach (var waypoint in proposal.Waypoints)
        {
            waypoint.Id = Guid.NewGuid();

            // Đổi MissionId thành đúng tên FK hiện tại nếu entity dùng tên khác.
            waypoint.MissionId = current.Id;

            current.Waypoints.Add(waypoint);
        }

        await Task.CompletedTask;
    }
    public Task DeleteAsync(
      Mission mission,
      CancellationToken cancellationToken = default)
    {
        db.Set<Waypoint>()
            .RemoveRange(mission.Waypoints);

        db.Missions.Remove(mission);

        return Task.CompletedTask;
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
                var result = await operation(cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}