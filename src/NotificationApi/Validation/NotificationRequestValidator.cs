using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Models.Configuration;
using NotificationApi.Models.DTOs;

namespace NotificationApi.Validation;

public class ValidationFailure
{
    public string PropertyName { get; }
    public string ErrorMessage { get; }

    public ValidationFailure(string propertyName, string errorMessage)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
    }
}

public class RequestValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationFailure> Errors { get; } = new();

    public NotificationPriority? ParsedPriority { get; set; }
    public NotificationType? ParsedType { get; set; }

    public void AddError(string propertyName, string errorMessage)
    {
        Errors.Add(new ValidationFailure(propertyName, errorMessage));
    }
}

public interface INotificationRequestValidator
{
    RequestValidationResult Validate(CreateNotificationRequest request);
}

public class NotificationRequestValidator : INotificationRequestValidator
{
    private readonly NotificationSettings _settings;
    private readonly TimeProvider _timeProvider;

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(NotificationPriority.Low),
        nameof(NotificationPriority.Normal),
        nameof(NotificationPriority.High)
    };

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250)
    );

    public NotificationRequestValidator(
        IOptions<NotificationSettings> settings,
        TimeProvider? timeProvider = null)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RequestValidationResult Validate(CreateNotificationRequest request)
    {
        var result = new RequestValidationResult();

        if (request == null)
        {
            result.AddError("Request", "The notification request body is required.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            result.AddError(nameof(request.CustomerId), "Customer ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            result.AddError(nameof(request.Type), "Notification type is required.");
        }
        else if (Enum.TryParse<NotificationType>(request.Type.Trim(), ignoreCase: true, out var parsedType)
                 && !int.TryParse(request.Type.Trim(), out _))
        {
            result.ParsedType = parsedType;
        }
        else
        {
            result.AddError(nameof(request.Type),
                $"Unsupported notification type '{request.Type}'. Supported types: {string.Join(", ", Enum.GetNames<NotificationType>())}.");
        }

        if (string.IsNullOrWhiteSpace(request.Recipient))
        {
            result.AddError(nameof(request.Recipient), "Recipient is required.");
        }
        else
        {
            var isEmailChannel = result.ParsedType == NotificationType.Email ||
                                 string.Equals(request.Type?.Trim(), nameof(NotificationType.Email), StringComparison.OrdinalIgnoreCase) ||
                                 result.ParsedType == null;

            if (isEmailChannel && !IsValidEmail(request.Recipient))
            {
                result.AddError(nameof(request.Recipient), $"'{request.Recipient}' is not a valid email address.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            result.AddError(nameof(request.Message), "Message is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Priority))
        {
            result.AddError(nameof(request.Priority), "Priority is required.");
        }
        else if (AllowedPriorities.Contains(request.Priority.Trim()) &&
                 Enum.TryParse<NotificationPriority>(request.Priority.Trim(), ignoreCase: true, out var parsedPriority))
        {
            result.ParsedPriority = parsedPriority;
        }
        else
        {
            result.AddError(nameof(request.Priority),
                $"Priority '{request.Priority}' is invalid. Allowed values: {string.Join(", ", AllowedPriorities)}.");
        }

        if (request.RequestedDeliveryTime.HasValue)
        {
            var nowUtc = _timeProvider.GetUtcNow();
            var tolerance = TimeSpan.FromSeconds(_settings.PastDeliveryTimeToleranceSeconds);
            var earliestAcceptable = nowUtc.Subtract(tolerance);

            if (request.RequestedDeliveryTime.Value < earliestAcceptable)
            {
                result.AddError(nameof(request.RequestedDeliveryTime),
                    $"Requested delivery time '{request.RequestedDeliveryTime.Value:O}' cannot be in the past (current server UTC: '{nowUtc:O}').");
            }
        }

        return result;
    }

    private static bool IsValidEmail(string email)
    {
        var trimmed = email.Trim();
        if (trimmed.Length is < 3 or > 254) return false;
        if (trimmed.Contains("..")) return false;
        if (!EmailRegex.IsMatch(trimmed)) return false;

        try
        {
            var addr = new MailAddress(trimmed);
            if (addr.Address != trimmed) return false;
            if (!addr.Host.Contains('.')) return false;
            if (addr.Host.StartsWith('.') || addr.Host.EndsWith('.')) return false;

            var parts = addr.Host.Split('.');
            if (parts.Any(p => string.IsNullOrWhiteSpace(p))) return false;
            var tld = parts.Last();
            if (tld.Length < 2) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}
