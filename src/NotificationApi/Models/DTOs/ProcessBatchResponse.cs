namespace NotificationApi.Models.DTOs;

public class ProcessBatchResponse
{
    public int EvaluatedCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public List<ProcessedNotificationSummary> ProcessedItems { get; set; } = new();
}

public class ProcessedNotificationSummary
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
