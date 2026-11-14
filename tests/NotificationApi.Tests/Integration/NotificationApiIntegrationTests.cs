using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationApi.Domain.Enums;
using NotificationApi.Models.DTOs;
using NotificationApi.Persistence;

namespace NotificationApi.Tests.Integration;

public class NotificationApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDatabaseName = $"IntegrationTestDb_{Guid.NewGuid():N}";

    public NotificationApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<NotificationDbContext>>();
                services.RemoveAll<NotificationDbContext>();

                services.AddDbContext<NotificationDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDatabaseName);
                });
            });
        });

        _client = _factory.CreateClient();
    }

    private static CreateNotificationRequest BuildSampleRequest(
        string customerId = "CUST-INT-101",
        string priority = "Normal",
        string recipient = "user@example.com") => new()
    {
        CustomerId = customerId,
        Type = "Email",
        Recipient = recipient,
        Subject = "Integration Test Alert",
        Message = "Test notification body content.",
        Priority = priority,
        RequestedDeliveryTime = null
    };

    [Fact]
    public async Task CreateNotification_WithValidRequest_Returns201CreatedAndLocationHeader()
    {
        var request = BuildSampleRequest();

        var response = await _client.PostAsJsonAsync("/api/notifications", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(request.CustomerId, created.CustomerId);
        Assert.Equal(request.Recipient, created.Recipient);
        Assert.Equal(NotificationStatus.Pending.ToString(), created.Status);
        Assert.Equal(request.Priority, created.Priority);
    }

    [Fact]
    public async Task CreateNotification_WithDuplicateRequestWithinWindow_Returns200OkWithExistingEntity()
    {
        var request = BuildSampleRequest(customerId: "CUST-DEDUP-001");

        var response1 = await _client.PostAsJsonAsync("/api/notifications", request);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var created1 = await response1.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(created1);

        var response2 = await _client.PostAsJsonAsync("/api/notifications", request);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(response2.Headers.Contains("X-Duplicate-Replay"));

        var created2 = await response2.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(created2);
        Assert.Equal(created1.Id, created2.Id);
    }

    [Fact]
    public async Task CreateNotification_WithInvalidRequest_Returns400BadRequestWithProblemDetails()
    {
        var invalidRequest = new CreateNotificationRequest
        {
            CustomerId = string.Empty,
            Type = "Email",
            Recipient = "notanemail",
            Message = string.Empty,
            Priority = "InvalidPriority"
        };

        var response = await _client.PostAsJsonAsync("/api/notifications", invalidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(nameof(CreateNotificationRequest.CustomerId), content);
        Assert.Contains(nameof(CreateNotificationRequest.Recipient), content);
        Assert.Contains(nameof(CreateNotificationRequest.Message), content);
        Assert.Contains(nameof(CreateNotificationRequest.Priority), content);
    }

    [Fact]
    public async Task GetById_WhenNotificationExists_Returns200Ok()
    {
        var request = BuildSampleRequest(customerId: "CUST-GET-BY-ID");
        var createResponse = await _client.PostAsJsonAsync("/api/notifications", request);
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/api/notifications/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.CustomerId, fetched.CustomerId);
    }

    [Fact]
    public async Task GetById_WhenNotificationDoesNotExist_Returns404NotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/notifications/{nonExistentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerNotifications_ReturnsAllNotificationsForCustomer()
    {
        var customerId = $"CUST-LIST-{Guid.NewGuid():N}";
        var req1 = BuildSampleRequest(customerId: customerId, recipient: "c1@test.com");
        var req2 = BuildSampleRequest(customerId: customerId, recipient: "c2@test.com");

        await _client.PostAsJsonAsync("/api/notifications", req1);
        await _client.PostAsJsonAsync("/api/notifications", req2);

        var queryResponse = await _client.GetAsync($"/api/notifications?customerId={customerId}");
        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        var list1 = await queryResponse.Content.ReadFromJsonAsync<List<NotificationResponse>>();
        Assert.NotNull(list1);
        Assert.Equal(2, list1.Count);

        var routeResponse = await _client.GetAsync($"/api/customers/{customerId}/notifications");
        Assert.Equal(HttpStatusCode.OK, routeResponse.StatusCode);
        var list2 = await routeResponse.Content.ReadFromJsonAsync<List<NotificationResponse>>();
        Assert.NotNull(list2);
        Assert.Equal(2, list2.Count);
    }

    [Fact]
    public async Task ProcessNotifications_ExecutesQueueAndHandlesProviderFailuresEndToEnd()
    {
        var customerId = $"CUST-PROCESS-{Guid.NewGuid():N}";

        var lowReq = BuildSampleRequest(customerId: customerId, priority: "Low", recipient: "low@domain.com");
        await _client.PostAsJsonAsync("/api/notifications", lowReq);

        var highReq = BuildSampleRequest(customerId: customerId, priority: "High", recipient: "high@domain.com");
        await _client.PostAsJsonAsync("/api/notifications", highReq);

        var failReq = BuildSampleRequest(customerId: customerId, priority: "Normal", recipient: "fail@domain.com");
        var failPostResp = await _client.PostAsJsonAsync("/api/notifications", failReq);
        var failCreated = await failPostResp.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(failCreated);

        var processResp = await _client.PostAsync("/api/notifications/process", null);
        Assert.Equal(HttpStatusCode.OK, processResp.StatusCode);

        var batchSummary = await processResp.Content.ReadFromJsonAsync<ProcessBatchResponse>();
        Assert.NotNull(batchSummary);
        Assert.True(batchSummary.EvaluatedCount >= 3);
        Assert.True(batchSummary.SentCount >= 2);
        Assert.True(batchSummary.FailedCount >= 1);

        var highIdx = batchSummary.ProcessedItems.FindIndex(i => i.Recipient == "high@domain.com");
        var lowIdx = batchSummary.ProcessedItems.FindIndex(i => i.Recipient == "low@domain.com");
        Assert.True(highIdx >= 0 && lowIdx >= 0);
        Assert.True(highIdx < lowIdx);

        var failedCheckResp = await _client.GetAsync($"/api/notifications/{failCreated.Id}");
        var failedNotification = await failedCheckResp.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(failedNotification);
        Assert.Equal(NotificationStatus.Failed.ToString(), failedNotification.Status);
        Assert.NotNull(failedNotification.FailureReason);
        Assert.NotNull(failedNotification.ProcessedAt);
    }
}
