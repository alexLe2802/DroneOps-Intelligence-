using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public sealed class UserRepository
    : GenericRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<User?> GetByEmailWithRoleAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail =
            email.Trim().ToLowerInvariant();

        return await DbSet
            .AsNoTracking()
            .Include(user => user.Role)
            .FirstOrDefaultAsync(
                user => user.Email.ToLower() == normalizedEmail,
                cancellationToken);
    }

    public async Task<User?> GetByIdWithRoleAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(user => user.Role)
            .FirstOrDefaultAsync(
                user => user.Id == userId,
                cancellationToken);
    }

    public async Task<(
        IReadOnlyList<User> Items,
        int TotalCount)> GetPagedWithRoleAsync(
            int pageNumber,
            int pageSize,
            string? search,
            string? role,
            CancellationToken cancellationToken = default)
    {
        IQueryable<User> query = DbSet
            .AsNoTracking()
            .Include(user => user.Role);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string normalizedSearch =
                search.Trim().ToLower();

            query = query.Where(user =>
                user.FullName.ToLower()
                    .Contains(normalizedSearch) ||
                user.Email.ToLower()
                    .Contains(normalizedSearch));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            string normalizedRole =
                role.Trim().ToLower();

            query = query.Where(user =>
                user.Role.Name.ToLower() == normalizedRole);
        }

        int totalCount =
            await query.CountAsync(cancellationToken);

        List<User> items = await query
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.FullName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

}