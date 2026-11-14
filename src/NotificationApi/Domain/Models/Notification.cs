using NotificationApi.Domain.Enums;

namespace NotificationApi.Domain.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public DateTimeOffset? RequestedDeliveryTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyHash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
}
