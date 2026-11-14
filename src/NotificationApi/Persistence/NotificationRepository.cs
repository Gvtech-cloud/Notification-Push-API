using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;

namespace NotificationApi.Persistence;

public class NotificationRepository : INotificationRepository
{
    private readonly NotificationDbContext _context;

    public NotificationRepository(NotificationDbContext context)
    {
        _context = context;
    }

    public async Task<Notification> CreateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await _context.Notifications.AddAsync(notification, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return notification;
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var list = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        return list
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
    }

    public async Task<Notification?> FindRecentDuplicateAsync(
        string idempotencyHash,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyHash);

        var candidates = await _context.Notifications
            .Where(n => n.IdempotencyHash == idempotencyHash)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(n => n.CreatedAt >= createdAfter)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<Notification>> GetPendingForProcessingAsync(
        DateTimeOffset asOfTime,
        int maxBatchSize,
        CancellationToken cancellationToken = default)
    {
        var pending = await _context.Notifications
            .Where(n => n.Status == NotificationStatus.Pending)
            .ToListAsync(cancellationToken);

        return pending
            .Where(n => n.RequestedDeliveryTime == null || n.RequestedDeliveryTime <= asOfTime)
            .OrderBy(n => n.Priority)
            .ThenBy(n => n.RequestedDeliveryTime ?? n.CreatedAt)
            .ThenBy(n => n.CreatedAt)
            .Take(maxBatchSize)
            .ToList();
    }

    public async Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _context.Notifications.Update(notification);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
