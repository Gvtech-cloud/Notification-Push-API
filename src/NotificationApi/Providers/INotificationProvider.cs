using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;

namespace NotificationApi.Providers;

public class ProviderSendResult
{
    public bool Success { get; }
    public string? MessageId { get; }
    public string? ErrorMessage { get; }

    private ProviderSendResult(bool success, string? messageId, string? errorMessage)
    {
        Success = success;
        MessageId = messageId;
        ErrorMessage = errorMessage;
    }

    public static ProviderSendResult Succeeded(string? messageId = null) =>
        new(true, messageId ?? $"msg_{Guid.NewGuid():N}", null);

    public static ProviderSendResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}

public interface INotificationProvider
{
    NotificationType SupportedType { get; }
    Task<ProviderSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
