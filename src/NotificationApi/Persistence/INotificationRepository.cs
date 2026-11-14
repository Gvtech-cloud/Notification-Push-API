using NotificationApi.Domain.Models;

namespace NotificationApi.Persistence;

public interface INotificationRepository
{
    Task<Notification> CreateAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default);
    Task<Notification?> FindRecentDuplicateAsync(string idempotencyHash, DateTimeOffset createdAfter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetPendingForProcessingAsync(DateTimeOffset asOfTime, int maxBatchSize, CancellationToken cancellationToken = default);
    Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default);
}
