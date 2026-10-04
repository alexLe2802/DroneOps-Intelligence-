using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class UavRepository
    : GenericRepository<UAV>,
      IUavRepository
{
    public UavRepository(
        AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(
            x => x.Code == code,
            cancellationToken);
    }

    public async Task<List<UAV>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(x => x.Pilot)
            .ThenInclude(x => x.User)
            .Where(x => x.Status == "Pending")
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
}