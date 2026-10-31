using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class UavRepository
    : GenericRepository<UAV>,
      IUavRepository
{
    public UavRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<bool> DeleteOwnedAsync(
        Guid uavId,
        Guid userId,
        bool canManageAll,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);
            // Pairing takes the same row lock before creating a permanent hardware
            // binding. This prevents deletion from racing a first-time claim.
            var uav = await DbSet.FromSqlInterpolated(
                    $"SELECT * FROM \"UAV\" WHERE \"Id\"={uavId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (uav is null || (!canManageAll && !await DbContext.Pilots.AnyAsync(
                    x => x.Id == uav.PilotId && x.UserId == userId, cancellationToken)))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (await DbContext.DroneHardwareIdentities.AnyAsync(
                    x => x.UavId == uavId, cancellationToken))
            {
                throw new InvalidOperationException(
                    "A UAV with a permanent flight-controller binding cannot be deleted. Deactivate it, or use the administrator-verified flight-controller replacement process.");
            }

            // Preserve missions and their audit history, but release the deleted aircraft assignment.
            await DbContext.Missions
                .Where(x => x.UAVId == uavId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UAVId, (Guid?)null), cancellationToken);

            // These records have required UAV foreign keys and cannot survive a physical UAV deletion.
            await DbContext.DroneConnections.Where(x => x.UavId == uavId).ExecuteDeleteAsync(cancellationToken);
            await DbContext.FlightCompletionEvents.Where(x => x.UAVId == uavId).ExecuteDeleteAsync(cancellationToken);
            await DbContext.TelemetryRecords.Where(x => x.UAVId == uavId).ExecuteDeleteAsync(cancellationToken);

            // Remove navigation notifications whose target no longer exists.
            await DbContext.Notifications
                .Where(x => x.ReferenceType == "UAV" && x.ReferenceId == uavId)
                .ExecuteDeleteAsync(cancellationToken);

            var deleted = await DbSet.Where(x => x.Id == uavId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted == 1;
        });
    }

    public async Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(
            x => x.Code == code,
            cancellationToken);
    }

    // Lấy danh sách UAV chờ duyệt: Hỗ trợ cả "Pending" (mới) và "Pending Review" (nếu có dữ liệu cũ)
    public async Task<List<UAV>> GetPendingAsync(
        string? keyword = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .AsNoTracking()
            .Include(x => x.Pilot)
            .ThenInclude(x => x.User)
            .Where(x => x.Status == "Pending" || x.Status == "Pending Review");

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim().ToLower();

            query = query.Where(x =>
                x.Code.ToLower().Contains(keyword) ||
                x.Name.ToLower().Contains(keyword) ||
                (x.Model != null && x.Model.ToLower().Contains(keyword)) ||
                x.Pilot.User.FullName.ToLower().Contains(keyword));
        }

        int safePage = page < 1 ? 1 : page;
        int safePageSize = pageSize < 1 ? 10 : pageSize;

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<UAV>> GetByPilotIdAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(x => x.Pilot)
            .ThenInclude(x => x.User)
            .Where(x => x.PilotId == pilotId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UAV?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.Pilot)
            .ThenInclude(x => x.User)
            .FirstOrDefaultAsync(
                x => x.Id == uavId,
                cancellationToken);
    }

    public async Task<(IReadOnlyList<UAV> Items, int TotalCount)> GetAllPagedAsync(
        string? keyword,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<UAV> query = DbSet
            .AsNoTracking()
            .Include(x => x.Pilot)
                .ThenInclude(x => x.User);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string normalizedKeyword = keyword.Trim().ToLowerInvariant();

            query = query.Where(uav =>
                uav.Code.ToLower().Contains(normalizedKeyword) ||
                uav.Name.ToLower().Contains(normalizedKeyword) ||
                (uav.Model != null && uav.Model.ToLower().Contains(normalizedKeyword)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            string normalizedStatus = status.Trim().ToLowerInvariant();

            // Nếu lọc theo "pending" hoặc "pending review" thì đều khớp được với "Pending" trong DB
            if (normalizedStatus == "pending" || normalizedStatus == "pending review")
            {
                query = query.Where(uav =>
                    uav.Status.ToLower() == "pending" ||
                    uav.Status.ToLower() == "pending review");
            }
            else
            {
                query = query.Where(uav =>
                    uav.Status.ToLower() == normalizedStatus);
            }
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<UAV> items = await query
            .OrderByDescending(uav => uav.CreatedAt)
            .ThenBy(uav => uav.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
