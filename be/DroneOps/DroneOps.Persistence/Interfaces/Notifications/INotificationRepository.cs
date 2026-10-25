using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces.Notifications;

public interface INotificationRepository : IGenericRepository<Notification>
{
    Task<List<Notification>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Notification?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetActiveOperationManagerIdsAsync(CancellationToken cancellationToken = default);
}
