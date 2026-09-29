using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailWithRoleAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<User?> GetByIdWithRoleAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}