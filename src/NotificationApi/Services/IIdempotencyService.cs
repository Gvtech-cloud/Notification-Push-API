using NotificationApi.Models.DTOs;

namespace NotificationApi.Services;

public interface IIdempotencyService
{
    string ComputeFingerprint(CreateNotificationRequest request);
}
