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
    string? keyword = null,
    int page = 1,
    int pageSize = 10,
    CancellationToken cancellationToken = default)
{
    var query = DbSet
        .AsNoTracking()
        .Include(x => x.Pilot)
        .ThenInclude(x => x.User)
        .Where(x => x.Status == "Pending");

    if (!string.IsNullOrWhiteSpace(keyword))
    {
        keyword = keyword.ToLower();

        query = query.Where(x =>
            x.Code.ToLower().Contains(keyword) ||
            x.Name.ToLower().Contains(keyword) ||
            (x.Model != null &&
             x.Model.ToLower().Contains(keyword)) ||
            x.Pilot.User.FullName.ToLower().Contains(keyword));
    }

    return await query
        .OrderByDescending(x => x.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
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