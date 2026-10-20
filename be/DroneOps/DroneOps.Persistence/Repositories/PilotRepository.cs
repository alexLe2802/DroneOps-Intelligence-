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

    public async Task<(IReadOnlyList<PilotUpdateRequest> Items,int TotalCount)>GetAllPilotRequestsAsync(
      int pageNumber,
      int pageSize,
      string? search,
      string? status,
      CancellationToken cancellationToken = default)
    {
        IQueryable<PilotUpdateRequest> query =
            DbContext
                .Set<PilotUpdateRequest>()
                .AsNoTracking()
                .Include(x => x.Pilot)
                    .ThenInclude(x => x.User)
                .Include(x => x.LicenseImages);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword =
                search.Trim().ToLower();

            query = query.Where(x =>
                x.Pilot.User.FullName
                    .ToLower()
                    .Contains(keyword)

                ||

                x.Pilot.User.Email
                    .ToLower()
                    .Contains(keyword)

                ||

                x.PilotLicenseNo
                    .ToLower()
                    .Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(
                x => x.Status == status);
        }

        int totalCount =
            await query.CountAsync(
                cancellationToken);

        List<PilotUpdateRequest> items =
            await query
                .OrderByDescending(
                    x => x.CreatedAt)
                .Skip(
                    (pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(
                    cancellationToken);

        return (items, totalCount);
    }

    public async Task<PilotUpdateRequest?> GetUpdateRequestForReviewAsync(
            Guid requestId,
            CancellationToken cancellationToken = default)
    {
        return await DbContext
            .Set<PilotUpdateRequest>()
            .Include(x => x.Pilot)
                .ThenInclude(x => x.User)
            .Include(x => x.LicenseImages)
            .FirstOrDefaultAsync(
                x => x.Id == requestId,
                cancellationToken);
    }

    public async Task<List<LicenseImage>>GetCurrentPilotLicenseImagesAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default)
    {
        return await DbContext
            .Set<LicenseImage>()
            .Where(x => x.PilotId == pilotId)
            .ToListAsync(cancellationToken);
    }


}