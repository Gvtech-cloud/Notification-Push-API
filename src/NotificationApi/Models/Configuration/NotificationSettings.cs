namespace NotificationApi.Models.Configuration;

public class NotificationSettings
{
    public const string SectionName = "NotificationSettings";

    public int DeduplicationWindowMinutes { get; set; } = 5;
    public int PastDeliveryTimeToleranceSeconds { get; set; } = 30;
    public int DefaultBatchSize { get; set; } = 50;
    public bool EnableBackgroundWorker { get; set; } = false;
    public int BackgroundWorkerIntervalSeconds { get; set; } = 10;
    public bool SimulateProviderFailure { get; set; } = false;
}
