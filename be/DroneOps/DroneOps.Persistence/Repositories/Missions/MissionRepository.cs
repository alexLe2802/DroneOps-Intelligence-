using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class MissionRepository : IMissionRepository
{
    private readonly AppDbContext _context;

    public MissionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        Mission mission,
        CancellationToken cancellationToken = default)
    {
        await _context.Missions.AddAsync(
            mission,
            cancellationToken);
    }

    public Task<bool> UavExistsAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        return _context.UAVs.AnyAsync(
            x => x.Id == uavId,
            cancellationToken);
    }
    public async Task<Mission?> GetByIdAsync(
    Guid missionId,
    CancellationToken cancellationToken = default)
    {
        return await _context.Missions
            .AsNoTracking()
            .Include(x => x.Waypoints)
            .Include(x => x.Creator)
            .Include(x => x.UAV)
            .FirstOrDefaultAsync(
                x => x.Id == missionId,
                cancellationToken);
    }
    public async Task<(List<Mission> Items, int TotalCount)> GetAllAsync(
     string? search,
     string? status,
     DateTime? fromDate,
     DateTime? toDate,
     int pageNumber,
     int pageSize,
     CancellationToken cancellationToken = default)
    {
        var query = _context.Missions
            .AsNoTracking()
            .Include(x => x.Waypoints)
            .AsQueryable();

        // Search theo Name hoặc Description
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(x =>
                x.Name.Contains(search) ||
                (x.Description != null && x.Description.Contains(search)));
        }

        // Filter status
        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim();

            query = query.Where(x => x.Status == status);
        }

        // Filter CreatedAt từ ngày
        if (fromDate.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= fromDate.Value);
        }

        // Filter CreatedAt đến ngày
        if (toDate.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
    public async Task<List<Mission>> GetByCreatorAsync(
    Guid creatorId,
    CancellationToken cancellationToken = default)
    {
        return await _context.Missions
            .AsNoTracking()
            .Include(x => x.Waypoints)
            .Where(x => x.CreatedBy == creatorId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Mission?> GetByIdForUpdateAsync(
     Guid missionId,
     CancellationToken cancellationToken = default)
    {
        return await _context.Missions
            .FirstOrDefaultAsync(
                x => x.Id == missionId,
                cancellationToken);
    }
    public async Task DeleteWaypointsByMissionIdAsync(
     Guid missionId,
     CancellationToken cancellationToken = default)
    {
        await _context.Waypoints
            .Where(x => x.MissionId == missionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddWaypointsAsync(
        IEnumerable<Waypoint> waypoints,
        CancellationToken cancellationToken = default)
    {
        await _context.Waypoints.AddRangeAsync(
            waypoints,
            cancellationToken);
    }
    public Task<bool> IsUavOwnedByUserAsync(
    Guid uavId,
    Guid userId,
    CancellationToken cancellationToken = default)
    {
        return _context.UAVs.AnyAsync(
            uav =>
                uav.Id == uavId &&
                uav.Pilot.UserId == userId,
            cancellationToken);
    }
    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}