# Customer Notification Processing API (.NET 8)

A robust, clean, and extensible **.NET 8 Web API** for receiving, validating, deduplicating, storing, and processing customer notifications.

Initially built with support for **Email notifications**, designed with modular abstractions to scale to other delivery channels (such as SMS and Push).

---

## 1. How to Run the Application

### Prerequisites
- [.NET 8 SDK or .NET 10 SDK](https://dotnet.microsoft.com/download)
- Compatible with Windows, Linux, and macOS

### Running the API
From the root directory:
```bash
cd src/NotificationApi
dotnet run
```
The API starts locally:
- **Swagger UI**: Navigate to `http://localhost:5000/` or `https://localhost:5001/` in your browser.
- **SQLite Database**: A persistent database file `notifications.db` is automatically initialized on startup via EF Core SQLite.

### Running the Tests
From the root directory:
```bash
dotnet test tests/NotificationApi.Tests/NotificationApi.Tests.csproj --verbosity normal
```
All **56 unit and integration tests** will execute and report results.

### Sample Request (cURL)
```bash
curl -X POST "http://localhost:5000/api/notifications" \
     -H "Content-Type: application/json" \
     -d '{
           "customerId": "CUST-101",
           "type": "Email",
           "recipient": "customer@example.com",
           "subject": "Order Confirmed",
           "message": "Your order #8765 has been confirmed.",
           "priority": "Normal"
         }'
```

---

## 2. Overall Design & Architecture

The solution follows a clean, modular architecture separating API endpoints, business logic, validation, persistence, and external provider abstractions:

```
src/NotificationApi/
├── Controllers/
│   └── NotificationsController.cs
├── Domain/
│   ├── Enums/
│   │   ├── NotificationType.cs
│   │   ├── NotificationPriority.cs
│   │   └── NotificationStatus.cs
│   └── Models/
│       └── Notification.cs
├── Models/
│   ├── DTOs/
│   │   ├── CreateNotificationRequest.cs
│   │   ├── NotificationResponse.cs
│   │   └── ProcessBatchResponse.cs
│   └── Configuration/
│       └── NotificationSettings.cs
├── Validation/
│   └── NotificationRequestValidator.cs
├── Services/
│   ├── IIdempotencyService.cs
│   ├── IdempotencyService.cs
│   ├── INotificationProcessingService.cs
│   ├── NotificationProcessingService.cs
│   └── NotificationBackgroundWorker.cs
├── Providers/
│   ├── INotificationProvider.cs
│   ├── ProviderSendResult.cs
│   └── SimulatedEmailNotificationProvider.cs
├── Persistence/
│   ├── NotificationDbContext.cs
│   ├── INotificationRepository.cs
│   └── NotificationRepository.cs
└── Program.cs
```

### Key Components

1. **Idempotency & Deduplication (`IdempotencyService`)**:
   - Computes a deterministic SHA-256 hash across canonical request fields (`CustomerId`, `Type`, normalized `Recipient`, `Subject`, `Message`, `Priority`, and `RequestedDeliveryTime`).
   - Before inserting, queries for an existing notification with the same fingerprint within the deduplication window (`DeduplicationWindowMinutes = 5`).
   - If an active duplicate is found (`Pending`, `Processing`, or `Sent`), the API returns `200 OK` with the existing notification entity and the header `X-Duplicate-Replay: true`. It avoids re-dispatching duplicate messages while maintaining idempotent semantics.
   - If a previous attempt ended in `Failed`, the client is permitted to retry.

2. **Priority Processing (`NotificationProcessingService`)**:
   - `NotificationPriority` enum is defined as `High = 1`, `Normal = 2`, `Low = 3`.
   - The query sorts pending notifications by:
     1. `Priority ASC` (High notifications are always processed ahead of Normal and Low).
     2. `RequestedDeliveryTime ?? CreatedAt ASC` (earliest scheduled or FIFO).
     3. `CreatedAt ASC` (FIFO tie-breaker for same priority).
   - Notifications scheduled for the future (`RequestedDeliveryTime > now`) are ignored until their scheduled time arrives.

3. **External Provider Abstraction (`INotificationProvider`)**:
   - `SimulatedEmailNotificationProvider` simulates real-world email dispatching, latency, and comprehensive logging.
   - Provider failure handling: Simulates failures when the recipient address contains `"fail"` or `"error"`, or when `NotificationSettings:SimulateProviderFailure` is toggled.
   - Fault tolerance: Any provider failure or unhandled exception is trapped gracefully. The notification status is transitioned to `Failed`, with `FailureReason` recorded and logged, without crashing the application or halting the processing of remaining notifications in the batch.

4. **Background Worker & On-Demand API**:
   - `POST /api/notifications/process`: Allows on-demand batch execution (ideal for manual triggers, tests, webhooks, or serverless cron jobs).
   - `NotificationBackgroundWorker`: A `BackgroundService` that periodically polls for pending notifications. It is configurable via `NotificationSettings:EnableBackgroundWorker` in `appsettings.json`.

---

## 3. Important Assumptions Made

1. **Email-Only Initial Scope with Open Extensibility**:
   - While the domain and providers are designed to handle multiple channels via `INotificationProvider`, only `Email` is enabled currently. Any unrecognized channel returns a descriptive `400 Bad Request` with allowed types.
2. **Deterministic Deduplication Window**:
   - The deduplication window defaults to **5 minutes** (configurable). If an identical request arrives within 5 minutes, it is recognized as a retry from network timeouts.
3. **UTC Timestamps & Clock Skew Tolerance**:
   - All timestamps (`CreatedAt`, `ProcessedAt`, `RequestedDeliveryTime`) are treated strictly in UTC.
   - A 30-second clock-skew grace period (`PastDeliveryTimeToleranceSeconds`) is allowed when validating that `RequestedDeliveryTime` is not in the past, accounting for network latency and minor server clock differences.
4. **Idempotency Semantics**:
   - Returning `200 OK` with the existing notification and an `X-Duplicate-Replay: true` header aligns with industry-standard REST idempotency patterns (e.g., Stripe, AWS).

---

## 4. Trade-Offs Made

| Choice | Rationale | Production Alternative |
| :--- | :--- | :--- |
| **SQLite with EF Core** | Zero-configuration, zero-dependency lightweight relational persistence that works out-of-the-box locally, while supporting full SQL querying, transactions, and EF Core migrations. | Distributed relational database (PostgreSQL, Azure SQL) or document store. |
| **In-Process Background Worker / API Trigger** | Simple, self-contained within a single process without requiring external queues or cluster coordinators. | Distributed task runner (Hangfire, Temporal) or cloud scheduler (AWS EventBridge, Azure Functions). |
| **Database-level Polling Queue** | Direct, clear to reason about, and adheres strictly to minimal complexity guidelines. | Dedicated message broker (RabbitMQ, Apache Kafka, Azure Service Bus) with priority queues. |
| **SHA-256 Payload Hash for Idempotency** | Protects against duplicate requests even if the caller does not generate or pass an explicit client-side `Idempotency-Key` header. | Explicit `Idempotency-Key` header supplied by caller + Redis distributed lock. |

---

## 5. Improvements for Production Readiness

If advancing this service to a high-volume production environment:

1. **Transactional Outbox & Message Broker**:
   - Decouple API ingestion from dispatch by persisting to an Outbox table in the same transaction, then publishing events to **RabbitMQ** or **Azure Service Bus** using priority queues.
2. **Exponential Backoff & Dead-Letter Queue (DLQ)**:
   - Implement retry policies with exponential backoff and jitter (e.g., using **Polly**) for transient provider errors (e.g., HTTP 429, 503).
   - Route permanently failed notifications to a Dead-Letter Queue for alerting and manual investigation.
3. **Distributed Deduplication Cache**:
   - Store idempotency fingerprints in **Redis** with distributed atomic locking (`RedLock`) to prevent race conditions during high-concurrency duplicate bursts.
4. **Resilience & Circuit Breaking**:
   - Wrap upstream notification provider calls in circuit breakers to stop cascading delays during downstream provider outages.
5. **Authentication & Rate Limiting**:
   - Secure endpoints with **OAuth2 / JWT Bearer** tokens.
   - Implement per-customer rate limiting (e.g., ASP.NET Core `RateLimiterMiddleware` or token bucket).
6. **Observability**:
   - Add structured OpenTelemetry tracing, Prometheus metrics (`notifications_sent_total`, `notifications_failed_total`, `delivery_latency_seconds`), and health checks (`/healthz`).

---

## 6. Unclear Requirements & Interpretations

1. **Duplicate Request Handling**:
   - *Ambiguity*: The prompt states: *"If the exact same request is submitted twice within a short period, the system should avoid sending the notification twice. How you identify and handle duplicates is up to you."*
   - *Interpretation*: Rather than rejecting retries with an error (e.g., `409 Conflict`), which would cause callers with network timeouts to treat the retry as a hard failure, the API returns `200 OK` along with the existing notification and an `X-Duplicate-Replay: true` header. This satisfies idempotency safely and gracefully.
2. **Delivery Time Equal to Current Time**:
   - *Ambiguity*: Mentioned as an edge case: *"delivery time exactly equal to the current time"*.
   - *Interpretation*: A requested delivery time equal to the current time UTC is considered **valid** and is immediately eligible for processing.
3. **Processing Mechanism**:
   - *Ambiguity*: The specification asks to *"Provide functionality to process pending notifications"* without specifying whether it should be an API endpoint, background worker, or both.
   - *Interpretation*: Both are provided! `POST /api/notifications/process` allows explicit on-demand batch execution, and `NotificationBackgroundWorker` can be enabled in configuration for automatic asynchronous polling.
#   N o t i f i c a t i o n - P u s h - A P I  
 #   N o t i f i c a t i o n - P u s h - A P I  
 