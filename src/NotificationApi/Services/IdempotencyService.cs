using System.Security.Cryptography;
using System.Text;
using NotificationApi.Models.DTOs;

namespace NotificationApi.Services;

public class IdempotencyService : IIdempotencyService
{
    public string ComputeFingerprint(CreateNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customerId = (request.CustomerId ?? string.Empty).Trim();
        var type = (request.Type ?? string.Empty).Trim().ToUpperInvariant();
        var recipient = (request.Recipient ?? string.Empty).Trim().ToLowerInvariant();
        var subject = (request.Subject ?? string.Empty).Trim();
        var message = (request.Message ?? string.Empty).Trim();
        var priority = (request.Priority ?? string.Empty).Trim().ToUpperInvariant();
        var deliveryTime = request.RequestedDeliveryTime.HasValue
            ? request.RequestedDeliveryTime.Value.ToUniversalTime().ToString("O")
            : "IMMEDIATE";

        var canonicalPayload = $"{customerId}|{type}|{recipient}|{subject}|{message}|{priority}|{deliveryTime}";
        var bytes = Encoding.UTF8.GetBytes(canonicalPayload);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
