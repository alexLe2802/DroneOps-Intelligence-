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
    public async Task<List<Mission>> GetAllAsync(
      CancellationToken cancellationToken = default)
    {
        return await _context.Missions
            .AsNoTracking()
            .Include(x => x.Waypoints)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
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