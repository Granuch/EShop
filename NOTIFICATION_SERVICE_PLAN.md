# NOTIFICATION_SERVICE_PLAN

## Repository Analysis Summary (Evidence Baseline)

The plan below is based on direct inspection of these existing services and shared modules:

- `Basket` (`API/Application/Domain/Infrastructure`) — production-ready minimal API + Redis + RabbitMQ + OpenTelemetry + Serilog.
- `Catalog` (`API/Application/Domain/Infrastructure`) — production-ready minimal API + EF Core/PostgreSQL + outbox + RabbitMQ.
- `Identity` (`API/Application/Domain/Infrastructure`) — production-ready controller API + ASP.NET Identity + PostgreSQL + outbox + RabbitMQ.
- `Ordering` (`API/Application/Domain/Infrastructure`) — production-ready minimal API + EF Core/PostgreSQL + outbox + RabbitMQ.
- `Payment` (`API/Application/Domain/Infrastructure`) — scaffolded, mostly TODO.
- `Notification` (`API/Application/Domain/Infrastructure`) — scaffolded worker + TODO consumers/email service.

Additional artifacts read:
- `docker-compose.yml`
- All existing service Dockerfiles
- Service `appsettings.json` + `appsettings.Development.json`
- Shared messaging contracts under `src/BuildingBlocks/EShop.BuildingBlocks.Messaging/Events`
- Shared infra patterns (`BaseDbContext`, `IdempotentConsumer`, `MassTransitServiceCollectionExtensions`, OpenTelemetry extension)
- Unit/integration test projects and representative test files

No `.env` or `.env.example` files are currently present in the repo.

---

## 1. Overview

### Purpose
`Notification Service` is responsible for asynchronous user-facing notifications triggered by cross-service domain events, with initial scope focused on email.

### Responsibilities
- Consume integration events from RabbitMQ via MassTransit.
- Resolve recipient information when not present in event payload.
- Compose and send email notifications.
- Persist notification delivery attempts and outcomes (for retries, audit, troubleshooting).
- Provide operational telemetry (logs, metrics, traces, health).

### Explicit Non-Responsibilities
- It should **not** own user identity lifecycle data (owned by Identity service).
- It should **not** own order/payment state transitions (owned by Ordering/Payment).
- It should **not** expose broad business APIs; only operational endpoints if needed.

### Fit in Current Architecture
Matches the existing layered microservice pattern:
- API/Host (entrypoint + host wiring)
- Application (use case handlers/consumers)
- Domain (contracts/entities)
- Infrastructure (email provider + persistence + transport wiring)

It should follow the same shared building blocks already used by Catalog/Identity/Ordering for:
- MediatR/behavior conventions where applicable
- Outbox/inbox idempotency concepts
- RabbitMQ setup via MassTransit
- OpenTelemetry + Serilog standardization

---

## 2. Technology Stack

Use the same runtime/platform baseline as existing services:

| Area | Version/Choice | Evidence from repo | Decision |
|---|---|---|---|
| Runtime | .NET `net10.0` | All service csproj target `net10.0` | Use `net10.0` |
| Host style | Worker Service + `BackgroundService` | `EShop.Notification.API` currently worker-style, `Worker.cs` extends `BackgroundService` | Keep worker host, use BackgroundService for long-running tasks |
| Messaging | MassTransit + RabbitMQ | Mature services use MassTransit + RabbitMQ (shared extension + service-specific consumers) | Use same |
| Email | MailKit `4.15.1` | `EShop.Notification.Infrastructure.csproj` already references MailKit | Keep MailKit |
| Logging | Serilog stack (`Serilog.AspNetCore 10.0.0`, enrichers, sinks) | Basket/Catalog/Identity/Ordering API projects | Align Notification with same Serilog profile |
| Tracing/Metrics | OpenTelemetry (`1.15.0` family) + Prometheus endpoint pattern | Existing mature services + shared OTel extension | Align with same |
| Validation | FluentValidation `12.1.1` (if commands/DTOs introduced) | Application projects use FluentValidation + ValidationBehavior | Reuse pattern |
| Persistence | EF Core 10 + Npgsql 10 (if delivery log stored in DB) | Catalog/Identity/Ordering infra | Use same for reliable logging/retries |
| Tests | NUnit 4.5.1 + Moq 4.20.72 + Microsoft.NET.Test.Sdk 18.3.0 | Existing unit/integration tests | Use same packages/conventions |

### Required Packages (Notification projects)

#### `EShop.Notification.API`
- `Microsoft.Extensions.Hosting` `10.0.2` (already present)
- `MassTransit.RabbitMQ` `9.0.0` (already present)
- Add (to align with mature services):
  - `Serilog.AspNetCore` `10.0.0`
  - `Serilog.Enrichers.Environment` `3.0.1`
  - `Serilog.Enrichers.Thread` `4.0.0`
  - `Serilog.Sinks.Console` `6.1.1`
  - `Serilog.Sinks.File` `7.0.0`
  - `Serilog.Sinks.Seq` `9.0.0`
  - `OpenTelemetry.Extensions.Hosting` `1.15.0`
  - `OpenTelemetry.Instrumentation.Http` `1.15.0`
  - `OpenTelemetry.Instrumentation.Runtime` `1.15.0`
  - `OpenTelemetry.Exporter.OpenTelemetryProtocol` `1.15.0`

#### `EShop.Notification.Application`
- `MassTransit` `9.0.0` (already present)
- If introducing command pipeline/validation:
  - `MediatR` `14.0.0`
  - `FluentValidation` `12.1.1`
  - `FluentValidation.DependencyInjectionExtensions` `12.1.1`

#### `EShop.Notification.Infrastructure`
- `MailKit` `4.15.1` (already present)
- Add:
  - `Microsoft.EntityFrameworkCore.Design` `10.0.2`
  - `Microsoft.EntityFrameworkCore.InMemory` `10.0.2`
  - `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.0`
  - optional cache package parity if needed later: `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.2`

---

## 3. Project Structure

## Proposed directory tree (aligned to existing service conventions)

```text
src/Services/Notification/
  EShop.Notification.API/
    Program.cs
    Worker.cs
    appsettings.json
    appsettings.Development.json
    appsettings.Development.Local.json (gitignored)
    Dockerfile
    Infrastructure/
      HealthChecks/
        NotificationReadinessHealthCheck.cs
        NotificationLivenessHealthCheck.cs
      Middleware/
        GlobalExceptionHandlerMiddleware.cs (if HTTP host is added)
      Configuration/
        NotificationApiSettings.cs

  EShop.Notification.Application/
    Extensions/
      ServiceCollectionExtensions.cs
    Consumers/
      OrderCreatedConsumer.cs
      OrderShippedConsumer.cs
      PaymentFailedConsumer.cs
      UserRegisteredConsumer.cs (optional welcome flow)
    Abstractions/
      IUserContactResolver.cs
      INotificationLogRepository.cs
    UseCases/
      SendOrderCreatedNotification/
      SendOrderShippedNotification/
      SendPaymentFailedNotification/

  EShop.Notification.Domain/
    Entities/
      NotificationLog.cs
    ValueObjects/
      NotificationContent.cs
      RecipientAddress.cs
    Interfaces/
      IEmailService.cs

  EShop.Notification.Infrastructure/
    Extensions/
      ServiceCollectionExtensions.cs
    Data/
      NotificationDbContext.cs
      NotificationDbContextFactory.cs
      Migrations/
        <timestamp>_InitialCreate.cs
        NotificationDbContextModelSnapshot.cs
    Repositories/
      NotificationLogRepository.cs
    Services/
      EmailService.cs
      UserContactResolver.cs
    Configuration/
      SmtpSettings.cs
      IdentityServiceSettings.cs
    Templates/
      order-created.html
      order-shipped.html
      payment-failed.html
      welcome.html
```

### Folder responsibilities
- `API`: host bootstrap only (DI, logging, telemetry, health, transport registration).
- `Application`: consumer logic + orchestration + business process flow.
- `Domain`: stable contracts and core notification models.
- `Infrastructure`: transport/email/data provider implementations.

---

## 4. Architecture & Design Patterns

### Architectural style
Follow the same **layered + DDD-lite + CQRS/MediatR pipeline** style already used by Catalog/Identity/Ordering:
- Domain layer: entities/events/contracts only.
- Application layer: handlers/consumers and orchestration.
- Infrastructure layer: DB, broker, external integrations.
- API/host layer: composition root.

### Layer interaction
`MassTransit Consumer -> Application orchestration -> Domain model -> Infrastructure services`

### Dependency Injection pattern
- Use `AddNotificationApplication()` and `AddNotificationInfrastructure()` extension methods like other services.
- Keep DI registration in extension classes, not in arbitrary files.

### Cross-cutting concerns
- Logging: Serilog with standard enrichers and request/operation correlation.
- Error handling: same RFC7807-compatible mapping style if HTTP surface exists; for worker, structured error logs + retries/dead-letter.
- Tracing: OpenTelemetry with `service.name=EShop.Notification.API`.
- Idempotency: use `IdempotentConsumer<TMessage, TDbContext>` from shared infrastructure.

---

## 5. Domain Model

### Existing domain artifacts (currently scaffolded)
- `NotificationLog` (status/type/retries/error)
- `IEmailService` with typed methods (`OrderConfirmationEmail`, `OrderShippedEmail`, `PaymentFailedEmail`, `WelcomeEmail`)

### Proposed production schema
Table: `NotificationLogs`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK |
| `EventId` | `uuid` | unique, for dedupe/audit |
| `EventType` | `varchar(200)` | required |
| `CorrelationId` | `varchar(100)` | nullable, indexed |
| `UserId` | `varchar(100)` | nullable, indexed |
| `RecipientEmail` | `varchar(320)` | required |
| `TemplateName` | `varchar(100)` | required |
| `Subject` | `varchar(300)` | required |
| `Status` | `int` | required (`Pending/Sent/Failed`) |
| `RetryCount` | `int` | default 0 |
| `LastError` | `varchar(4000)` | nullable |
| `ProviderMessageId` | `varchar(200)` | nullable |
| `CreatedAt` | `timestamp with time zone` | required |
| `SentAt` | `timestamp with time zone` | nullable |
| `UpdatedAt` | `timestamp with time zone` | nullable |

Indexes:
- `IX_NotificationLogs_Status_CreatedAt`
- `IX_NotificationLogs_EventId` (unique)
- `IX_NotificationLogs_CorrelationId`
- `IX_NotificationLogs_UserId`

Also include shared infra tables via `BaseDbContext`:
- `outbox_messages`
- `processed_messages`

Migration naming: existing timestamp format, e.g. `20260405120000_InitialCreate.cs`.

---

## 6. Configuration & Environment Variables

### Config strategy (match existing)
- Keep base defaults in `appsettings.json`.
- Keep local machine secrets in `appsettings.Development.Local.json` (not committed), matching existing local override pattern.
- Use env vars in Docker/production.

### Required settings

| Key | Example | Purpose |
|---|---|---|
| `ConnectionStrings:NotificationDb` | `Host=localhost;Port=5435;Database=eshop_notification;Username=postgres;Password=...` | Notification persistence |
| `ConnectionStrings:Redis` | `localhost:6379,password=...` | optional dedupe/cache |
| `RabbitMQ:Host` | `localhost` | broker host |
| `RabbitMQ:Port` | `5672` | broker port |
| `RabbitMQ:Username` | `...` | broker auth |
| `RabbitMQ:Password` | `...` | broker auth |
| `Smtp:Host` | `smtp.mailtrap.io` | SMTP server |
| `Smtp:Port` | `587` | SMTP port |
| `Smtp:Username` | `...` | SMTP auth |
| `Smtp:Password` | `...` | SMTP auth |
| `Smtp:UseSsl` | `true` | transport security |
| `Smtp:FromEmail` | `noreply@eshop.local` | sender |
| `Smtp:FromName` | `EShop` | sender display |
| `IdentityService:BaseUrl` | `http://identity-api:8080` | resolve user email |
| `IdentityService:TimeoutSeconds` | `5` | HTTP timeout |
| `OpenTelemetry:Enabled` | `true` | traces/metrics toggle |
| `OpenTelemetry:OtlpEndpoint` | `http://otel-collector:4317` | OTLP endpoint |
| `OpenTelemetry:SamplingRatio` | `0.1` | trace sample rate |
| `Serilog:MinimumLevel:Default` | `Information` | logging level |

### `.env.example` content (to add)

```env
# Notification DB
NOTIFICATION_POSTGRES_DB=eshop_notification
NOTIFICATION_POSTGRES_USER=postgres
NOTIFICATION_POSTGRES_PASSWORD=CHANGE_ME_notification_postgres_password
NOTIFICATION_POSTGRES_PORT=5435

# RabbitMQ
RABBITMQ_USERNAME=CHANGE_ME_rabbitmq_user
RABBITMQ_PASSWORD=CHANGE_ME_rabbitmq_password

# SMTP
NOTIFICATION_SMTP_HOST=smtp.mailtrap.io
NOTIFICATION_SMTP_PORT=587
NOTIFICATION_SMTP_USERNAME=CHANGE_ME_smtp_user
NOTIFICATION_SMTP_PASSWORD=CHANGE_ME_smtp_password
NOTIFICATION_SMTP_USE_SSL=true
NOTIFICATION_SMTP_FROM_EMAIL=noreply@eshop.local
NOTIFICATION_SMTP_FROM_NAME=EShop

# Identity service resolution
IDENTITY_SERVICE_BASE_URL=http://identity-api:8080
IDENTITY_SERVICE_TIMEOUT_SECONDS=5
```

---

## 7. Message Broker Integration

### Broker
RabbitMQ via MassTransit (already standard in all event-driven services).

### Consume events
Use these existing contracts from `EShop.BuildingBlocks.Messaging.Events`:
- `OrderCreatedEvent`
- `OrderShippedEvent`
- `PaymentFailedEvent`
- optionally `UserRegisteredIntegrationEvent` for welcome email

### Event envelope
Inherits from `IntegrationEvent`:
- `EventId`
- `OccurredOn`
- `CorrelationId`
- `Version`

### Queue naming
With existing `SnakeCaseEndpointNameFormatter(includeNamespace: false)`, queue names become consumer-based snake_case (e.g., `order_created_consumer`). Keep this convention.

### Consumer group/competing consumer model
MassTransit queue semantics already provide competing consumers. Scale by service replicas.

### DLQ/error handling
Use existing MassTransit + shared extension policy pattern:
- immediate retries (`UseMessageRetry` incremental)
- delayed redelivery (`UseDelayedRedelivery`)
- circuit breaker
- eventual move to error queue managed by MassTransit

### Emit events (optional)
Only emit if needed (e.g., `NotificationSentEvent`); not required for MVP.

---

## 8. Email Sending Implementation

### Provider
Use existing `MailKit` (`MailKit.Net.Smtp`) implementation in `Infrastructure/Services/EmailService.cs`.

### Recipient resolution
- If event has email (`OrderShippedEvent.UserEmail`), use it directly.
- If event omits email (`OrderCreatedEvent` intentionally omits PII), resolve via Identity service using `UserId`.
- `PaymentFailedEvent` currently has no `UserId` or email; resolve through Ordering service or require contract update (see Open Questions).

### Template approach
Prefer file-based HTML templates under `Infrastructure/Templates` with lightweight token replacement to avoid introducing new templating libraries.

### Retry logic
- Transport-level retries from MassTransit.
- Persist each attempt to `NotificationLogs`.
- Mark permanent failures after max retries and rely on error queue for manual replay.

### Delivery guarantee
At-least-once delivery:
- Message broker retries + idempotent consumer using `processed_messages`.
- Notification dedupe using unique `EventId` in `NotificationLogs`.

---

## 9. API Layer (if any)

Notification is primarily background/event-driven.

### Recommended minimal operational surface
- `GET /health`
- `GET /health/ready`
- `GET /health/live`
- optional `POST /api/v1/notifications/retry/{id}` (admin-only, later phase)

If HTTP endpoints are added, align with existing services:
- JWT auth settings shape from `JwtSettings`.
- Serilog request logging pattern.
- RFC7807 error response format.

If no HTTP host is exposed in MVP, health should still be published through worker health checks/logs for orchestrator probes.

---

## 10. Inter-Service Communication

### Email lookup flow
Primary strategy:
1. consume event
2. if email missing, call Identity API (or dedicated query endpoint) by `UserId`
3. fallback retry if unavailable

### Resilience policy
Match existing resilience posture:
- short timeout (5s)
- retry (3 attempts with backoff)
- circuit breaker to avoid cascading failures

### Service discovery/config
Use environment-configured base URLs (same as other service settings in compose).

---

## 11. Error Handling

### Error hierarchy
Reuse existing conventions from mature services:
- `ValidationException` -> 400
- domain/business errors -> 400
- auth errors -> 401/403 (if HTTP APIs exist)
- concurrency conflict -> 409
- unknown -> 500

### Worker error policy
- Consumer exceptions should be thrown after logging so MassTransit retry/dead-letter policies apply.
- Permanent mapping/template errors should be explicitly logged with event metadata and moved to failure status in DB.

### Logging on errors
Always include:
- `MessageId`
- `EventId`
- `CorrelationId`
- `EventType`
- `UserId` (if available)

---

## 12. Logging & Observability

### Logger setup
Adopt same Serilog profile from Basket/Catalog/Identity/Ordering:
- Console + rolling file + Seq sinks
- enrichers: environment, machine name, thread id, log context

### Log levels
- `Information`: event received, email sent, retry scheduled
- `Warning`: transient delivery failure, missing optional data
- `Error`: final failure, deserialization mismatch, provider outage
- `Debug`: template rendering internals, low-level diagnostics

### Trace/Correlation
- propagate `CorrelationId` from `IntegrationEvent` into log scopes and outbound HTTP headers.
- register custom activity source `EShop.Notification` in OpenTelemetry.

### Health
- readiness checks: RabbitMQ connectivity, SMTP connectivity (or config validity), DB connectivity.
- liveness check: process heartbeat.

---

## 13. Testing Strategy

Use existing test conventions (`NUnit + Moq + FluentAssertions + WebApplicationFactory` for integrations).

### Unit tests (Application/Domain)
Mock external interfaces (`IEmailService`, resolver, repository, publish endpoint).

#### Unit test cases (at least 5)
1. `OrderCreatedConsumer` sends confirmation when resolver returns valid email.
2. `OrderCreatedConsumer` does not duplicate send for same `EventId`.
3. `OrderShippedConsumer` uses `UserEmail` directly when provided.
4. `PaymentFailedConsumer` logs and marks failed when recipient cannot be resolved.
5. `EmailService` maps template tokens correctly for all email models.

### Integration tests (Infrastructure + host wiring)

#### Integration test cases (at least 5)
1. Host boots in `Testing` environment with in-memory DB.
2. Consumer receives event from test bus and persists `NotificationLog`.
3. SMTP mock/fake path records successful send.
4. Retry pipeline triggers on transient SMTP exception.
5. Duplicate message (`MessageId`) is ignored by idempotent consumer.

### API/Operational endpoint tests (if endpoints added)

#### Endpoint test cases (at least 5)
1. `GET /health` returns healthy when all dependencies up.
2. `GET /health/ready` fails when DB is unavailable.
3. `GET /health/live` remains healthy during transient SMTP outage.
4. admin retry endpoint requires authentication/role.
5. retry endpoint updates `NotificationLog` status.

### Test file conventions
Follow current repo style:
- Unit: `tests/Services/Notification/EShop.Notification.UnitTests/...*Tests.cs`
- Integration: `tests/Services/Notification/EShop.Notification.IntegrationTests/...*Tests.cs`

---

## 14. Docker & Deployment

### Dockerfile
Create `src/Services/Notification/EShop.Notification.API/Dockerfile` matching existing multi-stage .NET 10 style:
- `sdk:10.0` build/publish stage
- `aspnet:10.0` runtime stage
- non-root `appuser`
- health probe tooling (`curl`) if HTTP health endpoints are exposed

### docker-compose additions
Add:
- `notification-postgres` (if DB logging enabled)
- `notification-api` service with env vars:
  - `ConnectionStrings__NotificationDb`
  - `RabbitMQ__*`
  - `Smtp__*`
  - `IdentityService__BaseUrl`
  - `OpenTelemetry__*`
  - `Serilog__*`

Use existing profile style (`production`, `sandbox`).

### Kubernetes manifests
No existing k8s manifests found in repo; do not introduce k8s as mandatory in this phase.

---

## 15. Step-by-Step Implementation Checklist

1. Confirm Notification scope (OrderCreated, OrderShipped, PaymentFailed, optional UserRegistered).
2. Add Notification API logging packages to match mature services.
3. Add Notification infrastructure EF/Npgsql packages.
4. Create `NotificationDbContext` inheriting from `BaseDbContext`.
5. Add `DbSet<NotificationLog>` to `NotificationDbContext`.
6. Configure `NotificationLog` table mapping and indexes.
7. Add `NotificationDbContextFactory` for migrations.
8. Create initial migration for notification schema.
9. Apply migration locally against Notification DB.
10. Add `INotificationLogRepository` abstraction.
11. Implement `NotificationLogRepository` in infrastructure.
12. Add `SmtpSettings` options class.
13. Bind `SmtpSettings` in infrastructure DI extension.
14. Implement `EmailService.SendOrderConfirmationAsync`.
15. Implement `EmailService.SendOrderShippedAsync`.
16. Implement `EmailService.SendPaymentFailedAsync`.
17. Implement `EmailService.SendWelcomeEmailAsync`.
18. Add reusable internal mail send helper in `EmailService`.
19. Add HTML template files under `Infrastructure/Templates`.
20. Implement template loading/token rendering helper.
21. Add `IUserContactResolver` abstraction in application layer.
22. Implement `UserContactResolver` using typed `HttpClient`.
23. Configure `IdentityServiceSettings` and typed client timeout.
24. Implement `OrderCreatedConsumer` end-to-end.
25. Implement `OrderShippedConsumer` end-to-end.
26. Implement `PaymentFailedConsumer` end-to-end.
27. Optionally implement `UserRegisteredConsumer` for welcome emails.
28. Refactor consumers to use idempotent base consumer.
29. Register consumers in Notification messaging extension.
30. Add `AddNotificationApplication()` extension.
31. Add `AddNotificationInfrastructure()` extension.
32. Add `AddNotificationMessaging()` extension.
33. Wire API host (`Program.cs`) to use all extensions.
34. Replace placeholder `Worker` loop with meaningful background responsibility (or remove if unnecessary).
35. Add Serilog bootstrap + host logging configuration.
36. Add OpenTelemetry setup via shared extension.
37. Add health checks (RabbitMQ, DB, SMTP/config).
38. Add `appsettings.json` sections (RabbitMQ, SMTP, OpenTelemetry, Serilog, IdentityService).
39. Add `appsettings.Development.json` safe local defaults.
40. Add `appsettings.Development.Local.json` sample guidance for local secrets.
41. Add Dockerfile for Notification API service.
42. Extend `docker-compose.yml` with notification DB and service entries.
43. Add Notification unit test project.
44. Add Notification integration test project.
45. Write consumer unit tests for all event handlers.
46. Write email service unit tests for template mapping.
47. Write integration tests for event consumption and log persistence.
48. Write resilience tests (retry + idempotency).
49. Run full build across solution.
50. Run Notification tests + impacted service integration tests.
51. Validate end-to-end: publish `OrderCreatedEvent` and verify first email delivery.
52. Validate duplicate event handling does not send duplicate email.
53. Validate observability data in logs/metrics/traces.
54. Document runbook for failed notification replay.

---

## 16. Open Questions & Assumptions

### Assumptions made from codebase
1. Notification service should become production-grade like Catalog/Identity/Ordering (current Notification project is scaffolded). **Confirmed: yes (stakeholder-approved).**
2. `OrderCreatedEvent` intentionally excludes PII; Notification will resolve email through Identity service. **Confirmed: yes (stakeholder-approved).**
3. Team accepts adding Notification persistence (DB) for retry/audit parity with existing outbox/idempotency design. **Confirmed: yes (stakeholder-approved).**
4. Local development may use configuration-file secrets in local-only files, then replace for production. **Confirmed: yes (stakeholder-approved).**

### Open Questions (real decisions)
1. `PaymentFailedEvent` currently lacks `UserId`/email. Expand contract or call Ordering by `OrderId`?
   - **Decision:** Expand contract.
   - **Implementation directive:** Add `UserId` to `PaymentFailedEvent`.
   - **Rationale:** Avoid extra synchronous dependency on Ordering, reduce latency and failure points, and ensure event carries sufficient consumer data.

2. HTTP endpoints vs pure Worker?
   - **Decision:** Worker-first service with minimal HTTP surface only for probes.
   - **Implementation directive:** Keep only `/health/ready` and `/health/live` for MVP.
   - **Deferred:** Full API and `POST /notifications/retry/{id}` moved to phase 2.

3. Welcome email (`UserRegisteredIntegrationEvent`) — MVP or phase 2?
   - **Decision:** Phase 2.
   - **Rationale:** MVP prioritizes transactional notifications (order confirmation, shipment status, payment failure).

4. Retry window and delivery SLA
   - **Decision:**
     - 3 immediate retries, 5 seconds interval.
     - then 3 delayed redeliveries: 1 min / 5 min / 15 min.
   - **Expected max retry window:** ~21 minutes before DLQ.
   - **SLA target:** Notification delivered within 5 minutes under healthy dependencies.

5. SMTP provider for production
   - **Decision for current implementation:** Build and configure for Mailtrap now.
   - **Future production path:** Switch by config to SES/SendGrid SMTP-compatible endpoints without code changes.

6. Notification preferences / unsubscribe
   - **Decision:** Phase 2.
   - **Rationale:** Separate subdomain (data model + UI + compliance); not required for MVP transactional mail.

7. `NotificationSentEvent` for analytics
   - **Decision:** Do not add in MVP.
   - **Rationale:** Add only when a real downstream consumer (analytics/BI) exists.

8. PII retention in `NotificationLogs` (`RecipientEmail`)
   - **Decision:** Must be finalized with team/legal before production go-live.
   - **Technical baseline accepted for implementation plan:**
     - Encrypt `RecipientEmail` at rest (pgcrypto or application-level encryption).
     - Add retention/TTL cleanup policy (target: 90 days).
     - Do not store email body in `NotificationLogs`, only metadata.

---

## Final Recommendation

Implement Notification using the same mature architecture already proven in Catalog/Identity/Ordering (DI extension pattern, MassTransit conventions, idempotent consumers, Serilog, OpenTelemetry, health checks, NUnit testing). Avoid introducing new frameworks unless strictly required. Keep local secret convenience through local override config, and enforce production secret replacement via environment variables.