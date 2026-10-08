using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories.Missions;

public sealed class MissionSuggestionRepository
    : GenericRepository<MissionSuggestion>,
      IMissionSuggestionRepository
{
    public MissionSuggestionRepository(
        AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<Guid?> GetPilotIdByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Pilots
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<MissionSuggestion>>
        GetByPilotIdAsync(
            Guid pilotId,
            CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Where(x => x.PilotId == pilotId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<MissionSuggestion?> GetOwnedByIdAsync(
        Guid suggestionId,
        Guid pilotId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == suggestionId &&
                     x.PilotId == pilotId,
                cancellationToken);
    }
}