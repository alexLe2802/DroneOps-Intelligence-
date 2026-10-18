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

    public async Task<Pilot?> GetByUserIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken);
    }

    public async Task<bool> HasPendingUpdateRequestAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext
            .Set<PilotUpdateRequest>()
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.PilotId == pilotId &&
                    x.Status == "Pending",
                cancellationToken);
    }

    public async Task AddUpdateRequestAsync(
        PilotUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await DbContext
            .Set<PilotUpdateRequest>()
            .AddAsync(
                request,
                cancellationToken);
    }

}