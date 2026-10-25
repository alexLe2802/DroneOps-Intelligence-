using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories.Notifications;

public sealed class NotificationRepository(AppDbContext db) : GenericRepository<Notification>(db), INotificationRepository
{
    public Task<List<Notification>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default) =>
        DbSet.AsNoTracking()
            .Where(x => x.RecipientUserId == userId && (!unreadOnly || !x.IsRead))
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        DbSet.CountAsync(x => x.RecipientUserId == userId && !x.IsRead, cancellationToken);

    public Task<Notification?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        DbSet.SingleOrDefaultAsync(x => x.Id == id && x.RecipientUserId == userId, cancellationToken);

    public Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken = default) =>
        DbSet.Where(x => x.RecipientUserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, readAt), cancellationToken);

    public Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default) =>
        DbSet.AddRangeAsync(notifications, cancellationToken);

    public Task<List<Guid>> GetActiveOperationManagerIdsAsync(CancellationToken cancellationToken = default) =>
        DbContext.Users.AsNoTracking()
            .Where(x => x.IsActive && (x.Role.Name == "operation_manager" || x.Role.Name == "operations_manager"))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
}
