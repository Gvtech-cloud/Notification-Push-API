using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;
using NotificationApi.Models.Configuration;

namespace NotificationApi.Providers;

public record SentEmailRecord(Guid NotificationId, string Recipient, string? Subject, string Message, DateTimeOffset SentAt);

public class SimulatedEmailNotificationProvider : INotificationProvider
{
    private readonly ILogger<SimulatedEmailNotificationProvider> _logger;
    private readonly NotificationSettings _settings;
    private readonly ConcurrentBag<SentEmailRecord> _sentEmails = new();

    public NotificationType SupportedType => NotificationType.Email;

    public IReadOnlyCollection<SentEmailRecord> SentEmails => _sentEmails;

    public SimulatedEmailNotificationProvider(
        ILogger<SimulatedEmailNotificationProvider> logger,
        IOptions<NotificationSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<ProviderSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _logger.LogInformation(
            "Dispatching email notification {NotificationId} to {Recipient} with subject '{Subject}' (Priority: {Priority}).",
            notification.Id, notification.Recipient, notification.Subject ?? string.Empty, notification.Priority);

        await Task.Delay(10, cancellationToken);

        if (notification.Recipient.Contains("crash", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Simulating catastrophic provider network fault for {Recipient}", notification.Recipient);
            throw new InvalidOperationException("External SMTP network socket suddenly dropped.");
        }

        if (_settings.SimulateProviderFailure ||
            notification.Recipient.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            notification.Recipient.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            (notification.Subject?.Contains("[FORCE_FAIL]", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var error = "Simulated upstream mail provider rejection: 550 Mailbox unavailable or rejected by upstream MTA.";
            _logger.LogWarning("Failed to dispatch email {NotificationId} to {Recipient}: {Error}", notification.Id, notification.Recipient, error);
            return ProviderSendResult.Failed(error);
        }

        var messageId = $"email_{Guid.NewGuid():N}";
        _sentEmails.Add(new SentEmailRecord(notification.Id, notification.Recipient, notification.Subject, notification.Message, DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Email notification {NotificationId} successfully delivered to {Recipient}. External Message ID: {MessageId}",
            notification.Id, notification.Recipient, messageId);

        return ProviderSendResult.Succeeded(messageId);
    }
}
