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

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}