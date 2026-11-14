using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Models.Configuration;
using NotificationApi.Models.DTOs;
using NotificationApi.Tests.Helpers;
using NotificationApi.Validation;

namespace NotificationApi.Tests.Unit;

public class ValidationTests
{
    private readonly DateTimeOffset _referenceTime = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly TestTimeProvider _timeProvider;
    private readonly NotificationRequestValidator _validator;

    public ValidationTests()
    {
        _timeProvider = new TestTimeProvider(_referenceTime);
        var settings = Options.Create(new NotificationSettings
        {
            PastDeliveryTimeToleranceSeconds = 30
        });
        _validator = new NotificationRequestValidator(settings, _timeProvider);
    }

    private static CreateNotificationRequest CreateValidRequest() => new()
    {
        CustomerId = "CUST-101",
        Type = "Email",
        Recipient = "customer@example.com",
        Subject = "Order Confirmation",
        Message = "Your order #1234 has been confirmed.",
        Priority = "Normal",
        RequestedDeliveryTime = null
    };

    [Fact]
    public void Validate_WhenRequestIsValid_ReturnsSuccess()
    {
        var request = CreateValidRequest();

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(NotificationType.Email, result.ParsedType);
        Assert.Equal(NotificationPriority.Normal, result.ParsedPriority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenCustomerIdIsMissingOrWhitespace_FailsValidation(string? customerId)
    {
        var request = CreateValidRequest();
        request.CustomerId = customerId;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.CustomerId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sms")]
    [InlineData("Push")]
    [InlineData("CarrierPigeon")]
    public void Validate_WhenTypeIsMissingOrUnsupported_FailsValidation(string? type)
    {
        var request = CreateValidRequest();
        request.Type = type;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenRecipientIsMissingOrWhitespace_FailsValidation(string? recipient)
    {
        var request = CreateValidRequest();
        request.Recipient = recipient;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Recipient));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@missinguser.com")]
    [InlineData("missingdomain@")]
    [InlineData("missingdomain@.com")]
    [InlineData("double..dot@domain.com")]
    [InlineData("spaces in@email.com")]
    [InlineData("user@nodotdomain")]
    public void Validate_WhenEmailRecipientIsInvalid_FailsValidation(string invalidEmail)
    {
        var request = CreateValidRequest();
        request.Recipient = invalidEmail;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Recipient) && e.ErrorMessage.Contains("not a valid email address"));
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@company.co.uk")]
    [InlineData("user+tag@sub.domain.org")]
    public void Validate_WhenEmailRecipientIsValid_PassesValidation(string validEmail)
    {
        var request = CreateValidRequest();
        request.Recipient = validEmail;

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenMessageIsMissingOrWhitespace_FailsValidation(string? message)
    {
        var request = CreateValidRequest();
        request.Message = message;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Message));
    }

    [Theory]
    [InlineData("Low", NotificationPriority.Low)]
    [InlineData("low", NotificationPriority.Low)]
    [InlineData("Normal", NotificationPriority.Normal)]
    [InlineData("normal", NotificationPriority.Normal)]
    [InlineData("High", NotificationPriority.High)]
    [InlineData("HIGH", NotificationPriority.High)]
    public void Validate_WhenPriorityIsValid_PassesValidation(string priorityInput, NotificationPriority expected)
    {
        var request = CreateValidRequest();
        request.Priority = priorityInput;

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.ParsedPriority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Urgent")]
    [InlineData("Critical")]
    [InlineData("123")]
    public void Validate_WhenPriorityIsInvalid_FailsValidation(string? invalidPriority)
    {
        var request = CreateValidRequest();
        request.Priority = invalidPriority;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Priority));
    }

    [Fact]
    public void Validate_WhenRequestedDeliveryTimeIsExactlyEqualToCurrentTime_PassesValidation()
    {
        var request = CreateValidRequest();
        request.RequestedDeliveryTime = _referenceTime;

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenRequestedDeliveryTimeIsInTheFuture_PassesValidation()
    {
        var request = CreateValidRequest();
        request.RequestedDeliveryTime = _referenceTime.AddHours(2);

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenRequestedDeliveryTimeIsInThePastBeyondTolerance_FailsValidation()
    {
        var request = CreateValidRequest();
        request.RequestedDeliveryTime = _referenceTime.AddMinutes(-5);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.RequestedDeliveryTime) && e.ErrorMessage.Contains("cannot be in the past"));
    }

    [Fact]
    public void Validate_WhenRequestedDeliveryTimeIsWithinClockSkewTolerance_PassesValidation()
    {
        var request = CreateValidRequest();
        request.RequestedDeliveryTime = _referenceTime.AddSeconds(-10);

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
