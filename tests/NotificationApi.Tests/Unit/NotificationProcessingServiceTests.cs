using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;
using NotificationApi.Models.Configuration;
using NotificationApi.Persistence;
using NotificationApi.Providers;
using NotificationApi.Services;
using NotificationApi.Tests.Helpers;

namespace NotificationApi.Tests.Unit;

public class NotificationProcessingServiceTests
{
    private readonly DateTimeOffset _referenceTime = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly TestTimeProvider _timeProvider;
    private readonly NotificationDbContext _dbContext;
    private readonly NotificationRepository _repository;
    private readonly SimulatedEmailNotificationProvider _emailProvider;
    private readonly NotificationProcessingService _service;

    public NotificationProcessingServiceTests()
    {
        _timeProvider = new TestTimeProvider(_referenceTime);

        var dbOptions = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new NotificationDbContext(dbOptions);
        _repository = new NotificationRepository(_dbContext);

        var settings = Options.Create(new NotificationSettings
        {
            DefaultBatchSize = 50,
            SimulateProviderFailure = false
        });

        _emailProvider = new SimulatedEmailNotificationProvider(
            NullLogger<SimulatedEmailNotificationProvider>.Instance,
            settings);

        _service = new NotificationProcessingService(
            _repository,
            new INotificationProvider[] { _emailProvider },
            NullLogger<NotificationProcessingService>.Instance,
            settings,
            _timeProvider);
    }

    [Fact]
    public async Task ProcessPending_ProcessesHighPriorityBeforeNormalAndLowPriority()
    {
        var low = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-LOW",
            Type = NotificationType.Email,
            Recipient = "low@example.com",
            Message = "Low Priority Msg",
            Priority = NotificationPriority.Low,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime.AddMinutes(-10),
            IdempotencyHash = "hash-low"
        };

        var normal = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-NORMAL",
            Type = NotificationType.Email,
            Recipient = "normal@example.com",
            Message = "Normal Priority Msg",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime.AddMinutes(-5),
            IdempotencyHash = "hash-normal"
        };

        var high = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-HIGH",
            Type = NotificationType.Email,
            Recipient = "high@example.com",
            Message = "High Priority Msg",
            Priority = NotificationPriority.High,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime.AddMinutes(-1),
            IdempotencyHash = "hash-high"
        };

        await _repository.CreateAsync(low);
        await _repository.CreateAsync(normal);
        await _repository.CreateAsync(high);

        var result = await _service.ProcessPendingAsync();

        Assert.Equal(3, result.EvaluatedCount);
        Assert.Equal(3, result.SentCount);
        Assert.Equal(0, result.FailedCount);

        Assert.Equal(high.Id, result.ProcessedItems[0].Id);
        Assert.Equal(NotificationPriority.High.ToString(), result.ProcessedItems[0].Priority);

        Assert.Equal(normal.Id, result.ProcessedItems[1].Id);
        Assert.Equal(NotificationPriority.Normal.ToString(), result.ProcessedItems[1].Priority);

        Assert.Equal(low.Id, result.ProcessedItems[2].Id);
        Assert.Equal(NotificationPriority.Low.ToString(), result.ProcessedItems[2].Priority);

        var updatedHigh = await _repository.GetByIdAsync(high.Id);
        Assert.NotNull(updatedHigh);
        Assert.Equal(NotificationStatus.Sent, updatedHigh.Status);
        Assert.NotNull(updatedHigh.ProcessedAt);
        Assert.Null(updatedHigh.FailureReason);
    }

    [Fact]
    public async Task ProcessPending_MultipleNotificationsWithSamePriority_OrderedByFifoSchedule()
    {
        var n1 = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-1",
            Type = NotificationType.Email,
            Recipient = "user1@example.com",
            Message = "First Normal",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime.AddMinutes(-20),
            IdempotencyHash = "hash-1"
        };

        var n2 = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-2",
            Type = NotificationType.Email,
            Recipient = "user2@example.com",
            Message = "Second Normal",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime.AddMinutes(-10),
            IdempotencyHash = "hash-2"
        };

        await _repository.CreateAsync(n1);
        await _repository.CreateAsync(n2);

        var result = await _service.ProcessPendingAsync();

        Assert.Equal(2, result.EvaluatedCount);
        Assert.Equal(n1.Id, result.ProcessedItems[0].Id);
        Assert.Equal(n2.Id, result.ProcessedItems[1].Id);
    }

    [Fact]
    public async Task ProcessPending_NotificationsScheduledInFuture_AreNotProcessedPrematurely()
    {
        var immediate = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-NOW",
            Type = NotificationType.Email,
            Recipient = "now@example.com",
            Message = "Send Now",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            RequestedDeliveryTime = null,
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-now"
        };

        var future = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-FUTURE",
            Type = NotificationType.Email,
            Recipient = "future@example.com",
            Message = "Send Later",
            Priority = NotificationPriority.High,
            Status = NotificationStatus.Pending,
            RequestedDeliveryTime = _referenceTime.AddHours(1),
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-future"
        };

        await _repository.CreateAsync(immediate);
        await _repository.CreateAsync(future);

        var result = await _service.ProcessPendingAsync();

        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal(immediate.Id, result.ProcessedItems[0].Id);

        var futureDb = await _repository.GetByIdAsync(future.Id);
        Assert.NotNull(futureDb);
        Assert.Equal(NotificationStatus.Pending, futureDb.Status);

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var result2 = await _service.ProcessPendingAsync();

        Assert.Equal(1, result2.EvaluatedCount);
        Assert.Equal(future.Id, result2.ProcessedItems[0].Id);

        var futureDbAfter = await _repository.GetByIdAsync(future.Id);
        Assert.NotNull(futureDbAfter);
        Assert.Equal(NotificationStatus.Sent, futureDbAfter.Status);
    }

    [Fact]
    public async Task ProcessPending_WhenProviderFails_MarksStatusAsFailedWithoutCrashing()
    {
        var failingNotification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-FAIL",
            Type = NotificationType.Email,
            Recipient = "fail@example.com",
            Message = "This should fail",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-fail"
        };

        var healthyNotification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-HEALTHY",
            Type = NotificationType.Email,
            Recipient = "healthy@example.com",
            Message = "This should succeed",
            Priority = NotificationPriority.Low,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-healthy"
        };

        await _repository.CreateAsync(failingNotification);
        await _repository.CreateAsync(healthyNotification);

        var result = await _service.ProcessPendingAsync();

        Assert.Equal(2, result.EvaluatedCount);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.FailedCount);

        var failedDb = await _repository.GetByIdAsync(failingNotification.Id);
        Assert.NotNull(failedDb);
        Assert.Equal(NotificationStatus.Failed, failedDb.Status);
        Assert.NotNull(failedDb.FailureReason);
        Assert.Contains("Simulated upstream mail provider rejection", failedDb.FailureReason);
        Assert.NotNull(failedDb.ProcessedAt);

        var healthyDb = await _repository.GetByIdAsync(healthyNotification.Id);
        Assert.NotNull(healthyDb);
        Assert.Equal(NotificationStatus.Sent, healthyDb.Status);
    }

    [Fact]
    public async Task ProcessPending_WhenProviderThrowsException_HandlesGracefullyWithoutCrashing()
    {
        var crashingNotification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-CRASH",
            Type = NotificationType.Email,
            Recipient = "crash@example.com",
            Message = "This throws exception",
            Priority = NotificationPriority.High,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-crash"
        };

        var normalNotification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-NORMAL",
            Type = NotificationType.Email,
            Recipient = "user@example.com",
            Message = "Normal notification",
            Priority = NotificationPriority.Normal,
            Status = NotificationStatus.Pending,
            CreatedAt = _referenceTime,
            IdempotencyHash = "hash-normal"
        };

        await _repository.CreateAsync(crashingNotification);
        await _repository.CreateAsync(normalNotification);

        var result = await _service.ProcessPendingAsync();

        Assert.Equal(2, result.EvaluatedCount);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.FailedCount);

        var crashedDb = await _repository.GetByIdAsync(crashingNotification.Id);
        Assert.NotNull(crashedDb);
        Assert.Equal(NotificationStatus.Failed, crashedDb.Status);
        Assert.Contains("Provider error", crashedDb.FailureReason);

        var normalDb = await _repository.GetByIdAsync(normalNotification.Id);
        Assert.NotNull(normalDb);
        Assert.Equal(NotificationStatus.Sent, normalDb.Status);
    }
}
