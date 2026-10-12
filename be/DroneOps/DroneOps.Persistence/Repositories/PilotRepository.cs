using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class PilotRepository
    : GenericRepository<Pilot>,
      IPilotRepository
{
    public PilotRepository(
        AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<Pilot?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken);
    }
}