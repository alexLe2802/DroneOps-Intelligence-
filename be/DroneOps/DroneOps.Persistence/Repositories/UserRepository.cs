using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public class UserRepository
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
        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await DbSet
            .AsNoTracking()
            .Include(x => x.Role)
            .FirstOrDefaultAsync(
                x => x.Email.ToLower() == normalizedEmail,
                cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        return await DbSet
            .AsNoTracking()
            .AnyAsync(
                x => x.Email.ToLower() == normalizedEmail,
                cancellationToken);
    }
}