using NotificationApi.Models.DTOs;
using NotificationApi.Services;

namespace NotificationApi.Tests.Unit;

public class IdempotencyServiceTests
{
    private readonly IdempotencyService _service = new();

    [Fact]
    public void ComputeFingerprint_WithIdenticalRequests_ProducesIdenticalFingerprints()
    {
        var req1 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Subject = "Order #1",
            Message = "Confirmed",
            Priority = "Normal",
            RequestedDeliveryTime = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)
        };

        var req2 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Subject = "Order #1",
            Message = "Confirmed",
            Priority = "Normal",
            RequestedDeliveryTime = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)
        };

        var hash1 = _service.ComputeFingerprint(req1);
        var hash2 = _service.ComputeFingerprint(req2);

        Assert.Equal(hash1, hash2);
        Assert.False(string.IsNullOrWhiteSpace(hash1));
    }

    [Fact]
    public void ComputeFingerprint_NormalizesEmailCasingAndWhitespace_ProducesIdenticalFingerprints()
    {
        var req1 = new CreateNotificationRequest
        {
            CustomerId = " CUST-101 ",
            Type = "email",
            Recipient = "Customer@Example.COM",
            Subject = " Order #1 ",
            Message = " Confirmed ",
            Priority = "normal",
            RequestedDeliveryTime = null
        };

        var req2 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "EMAIL",
            Recipient = "customer@example.com",
            Subject = "Order #1",
            Message = "Confirmed",
            Priority = "NORMAL",
            RequestedDeliveryTime = null
        };

        var hash1 = _service.ComputeFingerprint(req1);
        var hash2 = _service.ComputeFingerprint(req2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeFingerprint_WhenMessageDiffers_ProducesDifferentFingerprints()
    {
        var req1 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Subject = "Notice",
            Message = "Message A",
            Priority = "Normal"
        };

        var req2 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Subject = "Notice",
            Message = "Message B",
            Priority = "Normal"
        };

        Assert.NotEqual(_service.ComputeFingerprint(req1), _service.ComputeFingerprint(req2));
    }

    [Fact]
    public void ComputeFingerprint_WhenPriorityDiffers_ProducesDifferentFingerprints()
    {
        var req1 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Message = "Text",
            Priority = "High"
        };

        var req2 = new CreateNotificationRequest
        {
            CustomerId = "CUST-101",
            Type = "Email",
            Recipient = "customer@example.com",
            Message = "Text",
            Priority = "Low"
        };

        Assert.NotEqual(_service.ComputeFingerprint(req1), _service.ComputeFingerprint(req2));
    }
}
