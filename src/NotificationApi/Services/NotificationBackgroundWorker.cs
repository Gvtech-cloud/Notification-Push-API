using Microsoft.Extensions.Options;
using NotificationApi.Models.Configuration;

namespace NotificationApi.Services;

public class NotificationBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationBackgroundWorker> _logger;
    private readonly IOptionsMonitor<NotificationSettings> _settings;

    public NotificationBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationBackgroundWorker> logger,
        IOptionsMonitor<NotificationSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationBackgroundWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentSettings = _settings.CurrentValue;

            if (currentSettings.EnableBackgroundWorker)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processingService = scope.ServiceProvider.GetRequiredService<INotificationProcessingService>();

                    var result = await processingService.ProcessPendingAsync(
                        currentSettings.DefaultBatchSize,
                        stoppingToken);

                    if (result.EvaluatedCount > 0)
                    {
                        _logger.LogInformation(
                            "Background worker cycle processed {Count} items (Sent: {Sent}, Failed: {Failed}).",
                            result.EvaluatedCount, result.SentCount, result.FailedCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error during notification background processing cycle.");
                }
            }

            var intervalSeconds = Math.Max(1, currentSettings.BackgroundWorkerIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }

        _logger.LogInformation("NotificationBackgroundWorker is stopping.");
    }
}
