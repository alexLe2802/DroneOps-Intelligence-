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
}