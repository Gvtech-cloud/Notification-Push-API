using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;
using NotificationApi.Models.Configuration;
using NotificationApi.Models.DTOs;
using NotificationApi.Persistence;
using NotificationApi.Providers;

namespace NotificationApi.Services;

public class NotificationProcessingService : INotificationProcessingService
{
    private readonly INotificationRepository _repository;
    private readonly IEnumerable<INotificationProvider> _providers;
    private readonly ILogger<NotificationProcessingService> _logger;
    private readonly NotificationSettings _settings;
    private readonly TimeProvider _timeProvider;

    public NotificationProcessingService(
        INotificationRepository repository,
        IEnumerable<INotificationProvider> providers,
        ILogger<NotificationProcessingService> logger,
        IOptions<NotificationSettings> settings,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _providers = providers;
        _logger = logger;
        _settings = settings.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProcessBatchResponse> ProcessPendingAsync(int? batchSize = null, CancellationToken cancellationToken = default)
    {
        var effectiveBatchSize = batchSize.GetValueOrDefault(_settings.DefaultBatchSize);
        if (effectiveBatchSize <= 0) effectiveBatchSize = _settings.DefaultBatchSize;

        var now = _timeProvider.GetUtcNow();
        var pendingItems = await _repository.GetPendingForProcessingAsync(now, effectiveBatchSize, cancellationToken);

        var response = new ProcessBatchResponse
        {
            EvaluatedCount = pendingItems.Count
        };

        if (pendingItems.Count == 0)
        {
            _logger.LogDebug("No pending notifications eligible for delivery as of {Now:O}.", now);
            return response;
        }

        _logger.LogInformation("Processing batch of {Count} notifications (as of {Now:O}).", pendingItems.Count, now);

        foreach (var notification in pendingItems)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var summary = await DispatchNotificationAsync(notification, cancellationToken);
            response.ProcessedItems.Add(summary);

            if (summary.Status == NotificationStatus.Sent.ToString())
            {
                response.SentCount++;
            }
            else
            {
                response.FailedCount++;
            }
        }

        _logger.LogInformation("Batch processing completed. Sent: {Sent}, Failed: {Failed}.", response.SentCount, response.FailedCount);
        return response;
    }

    public async Task<ProcessedNotificationSummary?> ProcessSingleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await _repository.GetByIdAsync(id, cancellationToken);
        if (notification == null) return null;

        return await DispatchNotificationAsync(notification, cancellationToken);
    }

    private async Task<ProcessedNotificationSummary> DispatchNotificationAsync(Notification notification, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        notification.Status = NotificationStatus.Processing;
        notification.AttemptCount++;
        await _repository.UpdateAsync(notification, cancellationToken);

        var provider = _providers.FirstOrDefault(p => p.SupportedType == notification.Type);
        if (provider == null)
        {
            notification.Status = NotificationStatus.Failed;
            notification.FailureReason = $"No active provider configured for channel '{notification.Type}'.";
            notification.ProcessedAt = now;
            await _repository.UpdateAsync(notification, cancellationToken);

            return new ProcessedNotificationSummary
            {
                Id = notification.Id,
                CustomerId = notification.CustomerId,
                Recipient = notification.Recipient,
                Priority = notification.Priority.ToString(),
                Status = notification.Status.ToString(),
                ErrorMessage = notification.FailureReason,
                ProcessedAt = notification.ProcessedAt
            };
        }

        try
        {
            var sendResult = await provider.SendAsync(notification, cancellationToken);

            if (sendResult.Success)
            {
                notification.Status = NotificationStatus.Sent;
                notification.FailureReason = null;
                notification.ProcessedAt = _timeProvider.GetUtcNow();
            }
            else
            {
                notification.Status = NotificationStatus.Failed;
                notification.FailureReason = sendResult.ErrorMessage ?? "Unknown provider failure.";
                notification.ProcessedAt = _timeProvider.GetUtcNow();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected provider exception during notification dispatch {NotificationId}", notification.Id);
            notification.Status = NotificationStatus.Failed;
            notification.FailureReason = $"Provider error: {ex.Message}";
            notification.ProcessedAt = _timeProvider.GetUtcNow();
        }

        await _repository.UpdateAsync(notification, cancellationToken);

        return new ProcessedNotificationSummary
        {
            Id = notification.Id,
            CustomerId = notification.CustomerId,
            Recipient = notification.Recipient,
            Priority = notification.Priority.ToString(),
            Status = notification.Status.ToString(),
            ErrorMessage = notification.FailureReason,
            ProcessedAt = notification.ProcessedAt
        };
    }
}
