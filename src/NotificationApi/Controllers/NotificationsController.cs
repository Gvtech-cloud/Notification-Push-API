using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NotificationApi.Domain.Enums;
using NotificationApi.Domain.Models;
using NotificationApi.Models.Configuration;
using NotificationApi.Models.DTOs;
using NotificationApi.Persistence;
using NotificationApi.Services;
using NotificationApi.Validation;

namespace NotificationApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _repository;
    private readonly INotificationRequestValidator _validator;
    private readonly IIdempotencyService _idempotencyService;
    private readonly INotificationProcessingService _processingService;
    private readonly NotificationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationRepository repository,
        INotificationRequestValidator validator,
        IIdempotencyService idempotencyService,
        INotificationProcessingService processingService,
        IOptions<NotificationSettings> settings,
        ILogger<NotificationsController> logger,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _validator = validator;
        _idempotencyService = idempotencyService;
        _processingService = processingService;
        _settings = settings.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [HttpPost]
    [ProducesResponseType(typeof(NotificationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(NotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var validation = _validator.Validate(request);
        if (!validation.IsValid)
        {
            var problemDetails = new ValidationProblemDetails();
            foreach (var err in validation.Errors)
            {
                if (!problemDetails.Errors.ContainsKey(err.PropertyName))
                {
                    problemDetails.Errors[err.PropertyName] = new[] { err.ErrorMessage };
                }
                else
                {
                    var existing = problemDetails.Errors[err.PropertyName];
                    problemDetails.Errors[err.PropertyName] = existing.Append(err.ErrorMessage).ToArray();
                }
            }

            problemDetails.Title = "Validation failed for notification request.";
            problemDetails.Status = StatusCodes.Status400BadRequest;
            return BadRequest(problemDetails);
        }

        var fingerprint = _idempotencyService.ComputeFingerprint(request);
        var dedupWindowStart = _timeProvider.GetUtcNow().AddMinutes(-_settings.DeduplicationWindowMinutes);

        var existingDuplicate = await _repository.FindRecentDuplicateAsync(fingerprint, dedupWindowStart, cancellationToken);
        if (existingDuplicate != null && existingDuplicate.Status != NotificationStatus.Failed)
        {
            _logger.LogInformation(
                "Duplicate notification request detected for Customer {CustomerId} (Hash: {Hash}). Returning existing notification {NotificationId}.",
                request.CustomerId, fingerprint, existingDuplicate.Id);

            Response.Headers["X-Duplicate-Replay"] = "true";
            return Ok(NotificationResponse.FromDomain(existingDuplicate));
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId!.Trim(),
            Type = validation.ParsedType!.Value,
            Recipient = request.Recipient!.Trim(),
            Subject = request.Subject?.Trim(),
            Message = request.Message!.Trim(),
            Priority = validation.ParsedPriority!.Value,
            Status = NotificationStatus.Pending,
            RequestedDeliveryTime = request.RequestedDeliveryTime?.ToUniversalTime(),
            CreatedAt = _timeProvider.GetUtcNow(),
            IdempotencyHash = fingerprint,
            AttemptCount = 0
        };

        var created = await _repository.CreateAsync(notification, cancellationToken);
        var response = NotificationResponse.FromDomain(created);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var notification = await _repository.GetByIdAsync(id, cancellationToken);
        if (notification == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Notification not found",
                Detail = $"No notification found with ID '{id}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(NotificationResponse.FromDomain(notification));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNotifications([FromQuery] string? customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing required query parameter",
                Detail = "Query parameter 'customerId' is required when listing notifications.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var notifications = await _repository.GetByCustomerIdAsync(customerId.Trim(), cancellationToken);
        var response = notifications.Select(NotificationResponse.FromDomain);
        return Ok(response);
    }

    [HttpGet("~/api/customers/{customerId}/notifications")]
    [ProducesResponseType(typeof(IEnumerable<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCustomerNotifications([FromRoute] string customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid customer ID",
                Detail = "Customer ID in route cannot be empty.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var notifications = await _repository.GetByCustomerIdAsync(customerId.Trim(), cancellationToken);
        var response = notifications.Select(NotificationResponse.FromDomain);
        return Ok(response);
    }

    [HttpPost("process")]
    [ProducesResponseType(typeof(ProcessBatchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProcessPending([FromQuery] int? batchSize, CancellationToken cancellationToken)
    {
        var result = await _processingService.ProcessPendingAsync(batchSize, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/process")]
    [ProducesResponseType(typeof(ProcessedNotificationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessSingle([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _processingService.ProcessSingleAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Notification not found",
                Detail = $"No notification found with ID '{id}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(result);
    }
}
