# Admin Audit Trail

**Scope:** who did what to which entity through an admin-reachable command, in Catalog, Identity, Ordering,
Payment and Notification. Added in admin-panel stage S15 (decision Q8a).

---

## Shape

There is no audit service and no audit event. Each service with a database keeps its **own** `audit_log` table and
writes it from a MediatR behavior; the gateway merges the five when an operator reads them.

```
admin request ──► service ──► AuditBehavior (outermost) ──► … TransactionBehavior … ──► handler
                                   │
                                   └─ after the pipeline returns or throws:
                                      one row, written through its OWN scope ──► <service>.audit_log

GET /api/v1/admin/audit ──► gateway ──► GET /api/v1/admin/audit on each of the five services (caller's token)
                                    ◄── one page each ── merged newest-first, per-service cursor
```

## What is recorded

Every execution of a command marked `IAuditedCommand` (BuildingBlocks.Application). A command is marked when an
**admin-authorized endpoint can send it**, including owner-or-admin ones such as cancelling an order — the question
"who changed this order?" has the same answer shape whether the customer or an operator did it. Customer-only commands
(login, checkout, own profile) and consumer-sent commands are not marked. Each service has an
`AuditedCommandClassificationTests` unit test listing every command as audited or not, with a reason, so a new command
cannot ship unclassified.

| Column | Meaning |
|---|---|
| `Id` | `bigint` identity — insertion order, and the paging cursor |
| `OccurredAt` | when the pipeline finished (UTC) |
| `Service` | `catalog`, `identity`, `ordering`, `payment`, `notification` |
| `Action` | the command's type name without `Command` (`UpdateProduct`) |
| `EntityType` / `EntityId` | declared by the command; a create's id comes from its result, a batch's is null |
| `ActorUserId` / `ActorName` | the caller's `NameIdentifier` and name/email claims |
| `CorrelationId` | the request's correlation id, cut to 100 characters |
| `Outcome` | `Succeeded`, `Rejected` (a failed `Result`) or `Failed` (an exception) |
| `ErrorCode` | the `Result` error code, or the exception's **type name** — never its message |
| `PayloadJson` | the request, redacted exactly as `LoggingBehavior` logs it |

**Failures are recorded, not only successes.** A rejected admin action is what an audit trail is for, and a
`Rejected` command may still have written something (`TransactionBehavior` commits on any non-exception return).
Authorization failures never reach MediatR and are not recorded here.

**The payload is the log line.** Both writers use `SafeRequestRenderer`, so a property redacted in the logs
(`[SensitiveData]` or a name such as `Password`) is redacted in the table, and nothing is stored that the Information
log did not already show. Unlike the logs, rows are kept indefinitely — there is no retention job.

## Why "outside the transaction" is an own scope

The behavior is registered first (`AddEShopAuditLog<TDbContext>(name)` precedes every other `Add*` in `Program.cs`), so
it runs after `TransactionBehavior` has committed or rolled back. Its writer resolves a **fresh** `DbContext` from its
own scope: the request's context may still track a failed command's changes, and after a constraint violation on
Postgres its transaction is aborted, so writing through it would either persist the failed changes or lose the row.
A failed audit write is logged at Error and never fails a command that has already committed.

## Reading it

- **Per service:** `GET /api/v1/admin/audit` (`audit.read`) — `before` (an id), `pageSize` (1–100, default 50),
  `actorUserId`, `action`, `entityType`, `entityId`, `outcome`, `from` (inclusive), `to` (exclusive). Filters are exact
  matches. Not routed by the gateway; reachable only inside the network.
- **Merged, through the gateway:** the same path and filters, with `cursor` instead of `before` and an optional
  `service`. The response is `{ items, nextCursor, unavailableServices }`. The cursor holds each service's own
  position, because ids from different services cannot be compared. The merge takes the newest *head* of each
  service's list rather than sorting their union, so every row arrives exactly once even where a service's id order and
  time order disagree slightly. A service that cannot be read is named in `unavailableServices` and keeps its position;
  its rows arrive on a later page.

Basket has no database and is not audited; its one admin write (outbox dead-letter replay) is not a MediatR command.

## Related

- [Security Architecture](security-architecture.md)
- [Databases](../06-infrastructure/databases.md)
- [API Gateway](../05-services/api-gateway.md)
