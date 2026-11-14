using NotificationApi.Domain.Models;

namespace NotificationApi.Models.DTOs;

public class NotificationResponse
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? RequestedDeliveryTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }

    public static NotificationResponse FromDomain(Notification notification)
    {
        return new NotificationResponse
        {
            Id = notification.Id,
            CustomerId = notification.CustomerId,
            Type = notification.Type.ToString(),
            Recipient = notification.Recipient,
            Subject = notification.Subject,
            Message = notification.Message,
            Priority = notification.Priority.ToString(),
            Status = notification.Status.ToString(),
            RequestedDeliveryTime = notification.RequestedDeliveryTime,
            CreatedAt = notification.CreatedAt,
            ProcessedAt = notification.ProcessedAt,
            FailureReason = notification.FailureReason,
            AttemptCount = notification.AttemptCount
        };
    }
}
