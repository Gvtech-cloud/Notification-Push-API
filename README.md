# Notification Processing API

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/Tests-56%20Passed-brightgreen?logo=xunit&logoColor=white)](https://xunit.net/)
[![Database](https://img.shields.io/badge/Database-SQLite%20%2F%20EF%20Core-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A robust, enterprise-grade **.NET 8 Web API** for validating, deduplicating, storing, and priority-processing customer notifications.

Initially built with support for **Email notifications**, the system is architected with modular abstractions (`INotificationProvider`) to seamlessly scale to additional channels (SMS, Push) without modifying core business workflows.

---

## Table of Contents

- [Quick Start](#quick-start)
- [API Endpoints](#api-endpoints)
- [Core Features](#core-features)
  - [1. Request Validation](#1-request-validation)
  - [2. Deduplication & Idempotency](#2-deduplication--idempotency)
  - [3. Priority-Based Processing](#3-priority-based-processing)
  - [4. Fault Tolerance & Provider Abstraction](#4-fault-tolerance--provider-abstraction)
- [System Architecture](#system-architecture)
- [Test Suite](#test-suite)
- [Design Decisions & Assumptions](#design-decisions--assumptions)
- [Production Readiness](#production-readiness)

---

## Quick Start

### Prerequisites
- [.NET 8 SDK or .NET 10 SDK](https://dotnet.microsoft.com/download)

### 1. Run the Application
```bash
# Navigate to the API project
cd src/NotificationApi

# Run the API
dotnet run
```

The service will automatically initialize the local SQLite database (`notifications.db`) and launch on `http://localhost:5000/`.

- **Swagger UI**: [http://localhost:5000/](http://localhost:5000/)

### 2. Run All Tests
```bash
dotnet test tests/NotificationApi.Tests/NotificationApi.Tests.csproj --verbosity normal
```

> [!NOTE]
> All **56 unit and integration tests** execute in under 2 seconds with zero external dependencies.

---

## API Endpoints

| HTTP Method | Route | Description | Status Codes |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/notifications` | Enqueue a new notification request | `201 Created`, `200 OK` (Duplicate), `400 Bad Request` |
| `GET` | `/api/notifications/{id}` | Retrieve a notification by GUID | `200 OK`, `404 Not Found` |
| `GET` | `/api/notifications?customerId={id}` | Retrieve all notifications for a customer | `200 OK`, `400 Bad Request` |
| `GET` | `/api/customers/{customerId}/notifications` | Convenience REST route for customer notifications | `200 OK`, `400 Bad Request` |
| `POST` | `/api/notifications/process` | Trigger batch processing of pending notifications | `200 OK` |
| `POST` | `/api/notifications/{id}/process` | Trigger immediate processing for a specific notification | `200 OK`, `404 Not Found` |

### Sample Enqueue Request (`POST /api/notifications`)
```json
{
  "customerId": "CUST-101",
  "type": "Email",
  "recipient": "customer@example.com",
  "subject": "Order Confirmed",
  "message": "Your order #1029 has been confirmed.",
  "priority": "High",
  "requestedDeliveryTime": "2026-11-15T10:00:00Z"
}
```

---

## Core Features

### 1. Request Validation
Incoming payloads undergo strict domain validation:
- `customerId`: Required, non-empty.
- `type`: Required; initially supports `Email`.
- `recipient`: Required; RFC-compliant email validation with DNS label and TLD rules.
- `message`: Required, non-empty.
- `priority`: Only `Low`, `Normal`, or `High` (case-insensitive).
- `requestedDeliveryTime`: Cannot be in the past (includes a 30-second clock skew tolerance for latency; times equal to current time are processed immediately).

If validation fails, the API responds with a structured `400 Bad Request` (`ValidationProblemDetails`).

### 2. Deduplication & Idempotency
To prevent duplicate dispatches caused by client retries or network timeouts:
- A deterministic **SHA-256 fingerprint** is computed from the normalized payload (`CustomerId`, `Type`, `Recipient`, `Subject`, `Message`, `Priority`, `RequestedDeliveryTime`).
- Before insertion, the system checks for an identical active notification (`Pending`, `Processing`, or `Sent`) created within the configurable deduplication window (default: **5 minutes**).
- If a duplicate is detected, the API returns the existing record with `200 OK` and includes the header:
  ```http
  X-Duplicate-Replay: true
  ```
- If a previous attempt ended in `Failed`, clients are permitted to retry.

### 3. Priority-Based Processing
Pending notifications are ordered and evaluated strictly by:
1. **Priority**: `High` (1) &rarr; `Normal` (2) &rarr; `Low` (3).
2. **Scheduled Time**: `RequestedDeliveryTime ?? CreatedAt` ascending.
3. **Creation Timestamp**: FIFO tie-breaker for identical priorities.

> [!IMPORTANT]
> Notifications scheduled in the future (`requestedDeliveryTime > now`) are deferred until their delivery timestamp is reached.

### 4. Fault Tolerance & Provider Abstraction
External providers are decoupled behind the `INotificationProvider` interface.

`SimulatedEmailNotificationProvider` simulates real-world mail dispatching, latency, and fault injection:
- Failures can be simulated on demand by sending to any recipient containing `fail` or `error` (e.g., `fail@example.com`).
- Upstream outages or unexpected network exceptions are caught safely.
- The notification status transitions to `Failed`, the error reason is stored in `FailureReason`, and remaining notifications in the queue continue processing uninterrupted.

---

## System Architecture

```
src/NotificationApi/
├── Controllers/
│   └── NotificationsController.cs           # HTTP endpoints and routing
├── Domain/
│   ├── Enums/
│   │   ├── NotificationType.cs              # Supported delivery channels
│   │   ├── NotificationPriority.cs          # High (1), Normal (2), Low (3)
│   │   └── NotificationStatus.cs            # Pending, Processing, Sent, Failed
│   └── Models/
│       └── Notification.cs                  # Core domain entity
├── Models/
│   ├── DTOs/
│   │   ├── CreateNotificationRequest.cs     # Input request contract
│   │   ├── NotificationResponse.cs          # Public response model
│   │   └── ProcessBatchResponse.cs          # Batch outcome summary
│   └── Configuration/
│       └── NotificationSettings.cs          # Configurable parameters
├── Validation/
│   └── NotificationRequestValidator.cs      # Input validator & RFC email check
├── Services/
│   ├── IIdempotencyService.cs               # Deterministic SHA-256 hasher
│   ├── IdempotencyService.cs
│   ├── INotificationProcessingService.cs    # Queue processing engine
│   ├── NotificationProcessingService.cs
│   └── NotificationBackgroundWorker.cs      # Background worker (configurable)
├── Providers/
│   ├── INotificationProvider.cs             # Channel provider interface
│   ├── ProviderSendResult.cs                # Success / Failure result wrapper
│   └── SimulatedEmailNotificationProvider.cs # Provider simulation & fault injection
├── Persistence/
│   ├── NotificationDbContext.cs             # EF Core SQLite DbContext
│   ├── INotificationRepository.cs           # Repository contract
│   └── NotificationRepository.cs            # SQLite database access
└── Program.cs                               # Dependency Injection & middleware pipeline
```

---

## Test Suite

The solution contains **56 automated tests** across unit and integration categories:

| Test Fixture | Count | Scope |
| :--- | :---: | :--- |
| `ValidationTests` | 23 | Valid payloads, missing fields, invalid emails, past delivery times, boundary conditions |
| `IdempotencyServiceTests` | 4 | SHA-256 fingerprinting, casing normalization, whitespace trimming, collisions |
| `NotificationProcessingServiceTests` | 5 | Priority sorting, FIFO tie-breaking, future delivery deferrals, provider failure handling |
| `NotificationApiIntegrationTests` | 7 | End-to-end HTTP pipeline (`WebApplicationFactory`), deduplication headers, status transitions |

To run the tests:
```bash
dotnet test tests/NotificationApi.Tests/NotificationApi.Tests.csproj --verbosity normal
```

---

## Design Decisions & Assumptions

| Decision | Rationale | Production Consideration |
| :--- | :--- | :--- |
| **SQLite with EF Core** | Provides zero-dependency relational persistence out-of-the-box locally, with indexes for customer lookups and queue sorting. | Transition to PostgreSQL or Azure SQL for distributed high-throughput workloads. |
| **On-Demand API & Background Worker** | Provides `POST /api/notifications/process` for deterministic test/manual triggers, plus an optional `BackgroundService` for automatic polling. | Decouple via distributed queue workers (Hangfire, Temporal, or AWS SQS / Azure Service Bus). |
| **Payload Hash Deduplication** | Detects client retries without requiring clients to supply a dedicated client-side idempotency header. | Complement with client-supplied `Idempotency-Key` headers stored in Redis. |
| **Idempotent 200 OK on Duplicate** | Rather than failing with `409 Conflict`, returning `200 OK` allows retrying clients to safely resume without error. | Aligns with standard REST patterns (e.g., Stripe, AWS). |

---

## Production Readiness

To scale this service to high-concurrency production environments:

- [ ] **Message Broker**: Introduce RabbitMQ or Azure Service Bus with priority queues to decouple ingestion from delivery.
- [ ] **Transactional Outbox**: Save events to an Outbox table in the same database transaction as the notification entity.
- [ ] **Distributed Cache**: Store deduplication fingerprints in Redis with distributed atomic locks (`RedLock`).
- [ ] **Resilience Policies**: Apply Polly circuit breakers, rate limits, and exponential backoff retries for upstream provider integrations.
- [ ] **Observability**: Integrate OpenTelemetry metrics (`notifications_processed_total`, `delivery_latency_ms`) and distributed tracing.
- [ ] **Authentication**: Enforce OAuth 2.0 / JWT Bearer authentication and role-based access control.

---

## License

This project is licensed under the [MIT License](LICENSE).
