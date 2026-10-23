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

    Task<bool> EmailExistsAsync(
        string email,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<User> Items, int TotalCount)>
       GetPagedWithRoleAsync(
           int pageNumber,
           int pageSize,
           string? search,
           string? role,
           CancellationToken cancellationToken = default);
}
