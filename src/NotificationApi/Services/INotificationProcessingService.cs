using NotificationApi.Models.DTOs;

namespace NotificationApi.Services;

public interface INotificationProcessingService
{
    Task<ProcessBatchResponse> ProcessPendingAsync(int? batchSize = null, CancellationToken cancellationToken = default);
    Task<ProcessedNotificationSummary?> ProcessSingleAsync(Guid id, CancellationToken cancellationToken = default);
}
