# Notification Service

Event-driven notification service for outbound communication workflows.

---

## Overview

Notification Service provides:
- Event consumption for business-triggered notifications
- Email delivery pipeline through configured SMTP settings
- Notification persistence and processing support
- Environment-aware configuration safeguards
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host/background processing |
| Database | PostgreSQL | Notification data persistence |
| Messaging | RabbitMQ + MassTransit | Event consumption |
| Email transport | SMTP provider (Mailpit/local or external SMTP) | Delivery channel |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Notification service follows layered architecture:

- `EShop.Notification.API`
- `EShop.Notification.Application`
- `EShop.Notification.Domain`
- `EShop.Notification.Infrastructure`

---

## Runtime Characteristics

### Startup Guards

`NotificationConfigurationGuard` runs before anything is registered, so a misconfigured deploy never starts: the
Identity base URL and password-reset URL, the SMTP host, sender and security mode, every email template, the
connection string, the internal API key, the JWT signing key (through the shared `JwtSecretGuard`) and a non-empty
JWT issuer and audience. Sandbox is guarded like Production; Production additionally requires an https,
non-loopback reset URL and TLS on SMTP.

The issuer and audience are required in *every* environment, including Testing, because `Program.cs` sets
`ValidateIssuer` and `ValidateAudience` — an empty one is not a lenient default but a service that rejects every
token it is ever shown while reporting healthy.

### Database Initialization

Service applies migrations at startup with retry logic for transient database readiness conditions.

### Messaging Integration

MassTransit consumers process notification-relevant events from the message broker.

---

## HTTP API

Until the admin-panel work the service had **no** HTTP API: `Program.cs` mapped only the health
endpoints, `/prometheus` and an information-free `GET /`. It now serves the delivery journal
that the admin panel's notification page is built on, and the operator actions on it.

The three journal reads require the `notifications.read` **permission** policy, not the `Admin` role.
Notification has never declared an `"Admin"` policy of its own, which is the case the permission
model exists for; `RolePermissionBundles` maps `Admin` to every permission, so an existing
administrator's token works unchanged, and a future operator role is a change to that one map.

| Method | Path | Returns |
|--------|------|---------|
| GET | `/api/v1/notifications` | Paged `NotificationSummaryDto`, newest first |
| GET | `/api/v1/notifications/{id}` | `NotificationDetailDto` — adds `lastError`, `correlationId`, `providerMessageId` |
| GET | `/api/v1/notifications/stats` | `NotificationStatsDto` — sent / failed / queued plus the full per-status breakdown |

The list and the stats share one filter surface, bound from the query string:

| Parameter | Meaning |
|-----------|---------|
| `status` | Repeatable. A union: `?status=Failed&status=Undeliverable` answers both. By **name**, case-insensitive; a number is refused. |
| `eventType` | The integration event's type name, case-insensitive. |
| `templateName` | The email template, case-insensitive. |
| `userId` | Exact match on the recipient's user id. |
| `email` | The recipient address, case-insensitive. |
| `from` / `to` | Inclusive bounds on `CreatedAt`. A value with no time zone is read as UTC. |
| `hasError` | `true` for rows carrying a failure reason, `false` for rows carrying none, omitted for both. |
| `pageNumber` / `pageSize` | List only. Defaults 1 and 20; `pageSize` is capped at 100. |

`status` is serialised by name, never by the stored integer — the column is
`HasConversion<int>()`, so the numbers are a storage detail and `3` is `Sending`, not `Failed`.

### Operator actions

The actions an operator takes on the journal. Every one declares the `notifications.manage`
permission, including the template test send: it writes nothing, but it emails an address the
caller chooses. The template list is a read (`notifications.read`). `notifications.read` alone
does **not** open the actions, so a support role can be given the journal without the buttons.

| Method | Path | Returns |
|--------|------|---------|
| POST | `/api/v1/notifications/{id}/resend` | **202** + `NotificationDetailDto` as it stands, `Location` → the detail |
| POST | `/api/v1/notifications/retry-failed` | **202** + `{ matching, limit, dispatchedIds, failedIds }` |
| POST | `/api/v1/notifications/{id}/mark-undeliverable` | 200 + `NotificationDetailDto`; body `{ "reason": "…" }`, required |
| GET | `/api/v1/notifications/templates` | `[{ name, eventType, resendable }]` |
| POST | `/api/v1/notifications/templates/{name}/test` | 200 + `{ templateName, providerMessageId }`; body `{ "email": "…", "name": "…" }` |

**A resend redelivers the event; it does not send the email itself.** The row keeps a copy of the
event it was built from (`Payload`, since migration `NotificationLogPayload`), and a resend sends
that event to Notification's **own** consumer queue (`notification_<consumer>`). The resend then
goes through exactly the path the first delivery took: the `NotificationLogs` claim, the attempt
lease, the row version, the retry policy and the error queue. The event is *sent*, never
*published*: publishing it again would reach every subscribed service, and Payment would open a
second payment for a re-published `OrderCreatedEvent`. That is why the answer is 202. The journal
shows the outcome once the consumer has run.

A resend is refused with **409** when the notification is final (`Notification.Final`), when an
attempt still holds its 5-minute lease (`Notification.AttemptInProgress`), or when no event was
kept (`Notification.NotResendable`). Two kinds of row keep no event:

- **password resets**, deliberately. The event carries a live reset token, and keeping it would
  leave that token at rest for the 90-day retention window. A customer whose reset email failed
  should request a new one.
- rows written before the payload column existed.

On a host with no message bus (Testing, or Development with no RabbitMQ) a resend answers **503**
`Notification.BusUnavailable`.

**Retry-failed** dispatches up to `limit` (1–100, default 100) **Failed** notifications that kept
their event, oldest first, narrowed by an optional JSON body: `eventType`, `templateName`,
`userId`, `email`, `from`, `to`. It has no status filter; the request only ever means "the failed
ones". If `matching` is larger than the ids returned, call again. A second call made before the
first batch has been processed can dispatch the same rows twice. That is safe: every copy carries
the same `EventId`, so the consumer's claim lets one attempt through and acknowledges the others.

**Mark-undeliverable** ends a notification for good. The reason is stored after the prefix
`Marked undeliverable by an operator: `, so the journal can tell an operator's decision from the
delivery path's. It is permitted from Pending, Failed, or a Sending row whose lease has expired.
It is refused (409) for a final row and for a live attempt, which may already have sent the email.
If a delivery claims the row between the operator's read and save, the row version turns the save
into a 409 `ConcurrencyConflict` instead of overwriting `Sent`.

**Test send** renders the template with sample data (an all-zero order id, and a reset link to the
real page with a token no account holds) and sends it through the same `IEmailService` a customer's
email uses, synchronously. It writes no journal row. If the mail server refuses or cannot be reached,
the answer is **503** `Notification.TestSendFailed`. It is not 502, because the gateway rewrites
every 502.

### Authentication, CORS and rate limiting

Gaining a web surface meant gaining the rest of the stack every other service already had:

- **JWT bearer authentication** against `JwtSettings:SecretKey` / `Issuer` / `Audience`, with
  `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` and zero clock skew.
- **CORS** through the shared `CorsOriginGuard`, validated while the host is composed rather than
  inside the `AddPolicy` lambda that CORS builds lazily.
- **A global rate limiter** partitioned per client address, off under `Testing` unless
  `RateLimiting:EnableInTesting` is set, reading the same `RateLimiting:Global:*` keys as Catalog,
  Identity, Ordering and Payment.
- **Forwarded headers**, which were already registered but had nothing reading a client address;
  the rate limiter's partition key now does.
- **RFC 7807 error responses** via `AddEShopProblemDetails(o => o.AddCommon().AddEfConcurrency().AddMalformedJsonBody())`,
  with `ThrowOnBadRequest` on. No `AddNotFound()`: every 404 is a `Result` error mapped by the
  endpoint, as Basket's are. `AddEfConcurrency()` and `AddMalformedJsonBody()` arrived with the
  operator actions, which save a row under its row version and take JSON bodies.
- **OpenAPI and Scalar** (`/openapi/v1.json`, `/scalar/v1`) in every environment except Production,
  through the shared `EShopApiDocs` rule. With no first-party admin client, this document is the
  contract surface for whoever builds one.

Health, metrics and `GET /` stay anonymous — a readiness probe behind a bearer token makes every
Kubernetes probe fail in a way that looks like the service being unhealthy.

### Gateway route

`/api/v1/notifications/{**catch-all}` → `notification-cluster`
(`http://notification-api:8080/`), `AuthorizationPolicy: Admin`, behind
`NotificationProxyGuardMiddleware` for the request-body cap and the 502→503 rewrite. The gateway's
role check and the service's permission check are two different questions and both must pass.

Admin-reachable commands are recorded in this service's `audit_log` and served on `GET /api/v1/admin/audit`
(`audit.read`); see [Admin Audit Trail](../03-architecture/audit-log.md).

---

## Workflow Role

Notification service reacts to events emitted by other services and handles asynchronous delivery logic rather than blocking upstream request paths.

---

## Health and Telemetry

Notification service exposes:
- `/health` (every check, including SMTP)
- `/health/ready` (database + RabbitMQ)
- `/health/live`
- `/prometheus`
- `/metrics`

And emits structured logs, traces, and metrics for operational diagnostics.

---

## Operational Notes

- Keep SMTP settings environment-specific and validated.
- Monitor consumer failures and delivery backlogs.
- Treat notification templates/content mapping as contract-sensitive behavior.

---

## Related Documents

- [Ordering Service](ordering-service.md)
- [Identity Service](identity-service.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 2.2  
**Last Updated**: 2026-09-21
