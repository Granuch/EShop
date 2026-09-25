# Admin platform endpoints (served by the gateway)

Four admin endpoints are answered by the API gateway itself rather than proxied to one service. Each one asks the
services behind it, in parallel, and merges the answers:

- [`GET /api/v1/admin/audit`](#get-apiv1adminaudit): the admin audit trail across every audited service;
- [`GET /api/v1/admin/health`](#get-apiv1adminhealth): the health of every component, for the System page;
- [`GET /api/v1/admin/settings`](#get-apiv1adminsettings): the read-only store settings;
- [`GET /api/v1/admin/feature-flags`](#get-apiv1adminfeature-flags): the read-only feature flags.

**Verified at:** `0d87f3b` (`feature/admin-panel`, 2026-09-25). The gateway changed in `f507f85` (operator-only
notices); none of these endpoints did. Every endpoint was checked against the source
(`EShop.ApiGateway/AuditLog/*`, `EShop.ApiGateway/SystemAdmin/*`, `EShop.BuildingBlocks.Infrastructure/Auditing/*`)
and the gateway's OpenAPI document, and called on the compose `sandbox` stack, including with one service paused so
that it could not answer. Shared rules (errors, auth, CORS) are in [conventions.md](conventions.md).

## Auth

| Endpoint | Gateway | Services |
|---|---|---|
| `GET /api/v1/admin/audit` | permission `audit.read` | each service checks `audit.read` again on its own slice |
| `GET /api/v1/admin/health` | permission `system.manage` | none: each service's `/health` is anonymous |
| `GET /api/v1/admin/settings` | permission `system.manage` | Ordering and Payment check `system.manage` again |
| `GET /api/v1/admin/feature-flags` | permission `system.manage` | Payment checks `system.manage` again |

- The gateway forwards the caller's bearer token to the services, so the service half is a real second check. Today
  only the `Admin` role holds these permissions ([conventions.md §5](conventions.md#5-permissions-and-admin-access)).
- Anonymous → **401**, a signed-in non-admin → **403**, both from the gateway with an empty body (verified on all
  four).
- **Rate limit:** the gateway's global limiter only (100 requests per 60 s per client IP). Each call also costs one
  request per service it reads, against that service's own global limiter.

**Timeouts and unavailable services.** The gateway gives each service **5 seconds**, and asks them all at once.
A service that fails, times out or answers something unreadable **never fails the page**: the endpoint answers 200
without that service's part and names it (`unavailableServices`, or `reachable: false` on the health page). Observed
with Payment paused: all four endpoints answered 200 in about 5.3 s, each naming Payment as unavailable.

The services serve their own audit, settings and feature-flags endpoints at the same paths; those are not reachable
through the gateway. Their slices are documented with each service ([notification.md](notification.md#not-callable-by-clients),
[payment.md](payment.md#not-callable-by-clients), [ordering.md](ordering.md#not-callable-by-clients),
[catalog.md](catalog.md#not-callable-by-clients), [identity.md](identity.md#not-callable-by-clients)).

## Contents

- [Audit trail](#get-apiv1adminaudit)
- [System health](#get-apiv1adminhealth)
- [Settings](#get-apiv1adminsettings)
- [Feature flags](#get-apiv1adminfeature-flags)
- [Types](#types)
- [Frontend notes](#frontend-notes)

---

## Admin panel

### `GET /api/v1/admin/audit`

Every admin action recorded by Catalog, Identity, Notification, Ordering and Payment, newest first, merged into one
list. Basket keeps no audit trail. Source: `AuditLogFanOut`, `AuditLogMerge`, `AuditLogCursor`.

**What is recorded:** one row each time an audited command runs (the admin writes in each service; the service files
say which), with its outcome:

| `outcome` | Meaning | `errorCode` |
|---|---|---|
| `Succeeded` | The command succeeded | `null` |
| `Rejected` | The command refused, for example a validation failure or a business rule. **Something may still have been written**; do not read it as "nothing changed" | The refusal's `errorCode`, for example `"Validation.Failed"`, `"Notification.Final"` |
| `Failed` | The command threw | The exception's **type name**, for example `"OperationCanceledException"` (observed) |

A batch command (Catalog's bulk actions) writes one row per item. Reads are not recorded, and neither is Payment's
`create-intent`.

**Query** [`AuditLogQuery`](#auditlogquery); all optional:

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `cursor` | string | — | The previous page's `nextCursor`. Omit for the newest page |
| `pageSize` | integer | `50` | 1–100 |
| `service` | string | all five | One of `catalog`, `identity`, `notification`, `ordering`, `payment`; any case, surrounding spaces ignored |
| `actorUserId` | string | — | **Exact, case-sensitive**; at most 200 characters |
| `action` | string | — | **Exact, case-sensitive**, the command name without `Command` (`RefundPayment`; `refundpayment` matches nothing); at most 100 characters |
| `entityType` | string | — | **Exact, case-sensitive** (`Product`, `Order`, `Payment`, `Notification`); at most 100 characters |
| `entityId` | string | — | **Exact, case-sensitive**: a GUID in upper case matches nothing; at most 200 characters |
| `outcome` | string | — | `Succeeded`, `Rejected` or `Failed`, any case. A number is refused |
| `from` | date-time | — | **Inclusive** lower bound on `occurredAt`. A value without a zone is read as UTC |
| `to` | date-time | — | **Exclusive** upper bound. Must be later than `from`; `from` = `to` is refused |

**Keep every parameter the same while you page**, and change only `cursor`. A cursor records a position per service
under the filters it was issued with.

**200** [`AuditLogPage`](#auditlogpage). Captured (`?pageSize=3`, the third item shortened):

```json
{"items":[
  {"id":44,"occurredAt":"2026-09-25T17:00:21.078163Z","service":"payment","action":"RefundPayment",
   "entityType":"Payment","entityId":"156da51d-0b25-4c09-95b6-c909276df3b9",
   "actorUserId":"16459cc5-f155-4ef2-a782-b611abab746e","actorName":"admin@eshop.com",
   "correlationId":"d00041d0b42d4defa50088f3a4903077","outcome":"Succeeded","errorCode":null,
   "payloadJson":"{\"paymentId\":\"156da51d-0b25-4c09-95b6-c909276df3b9\",\"amount\":null,\"reason\":null}"},
  {"id":43,"occurredAt":"2026-09-25T16:58:28.019223Z","service":"payment","action":"ReplayFailedStripeWebhooks",
   "entityType":"StripeWebhook","entityId":null,"actorUserId":"16459cc5-f155-4ef2-a782-b611abab746e",
   "actorName":"admin@eshop.com","correlationId":"011c47f71a3744bcaad6a42cc8e55fe6","outcome":"Rejected",
   "errorCode":"Validation.Failed",
   "payloadJson":"{\"ids\":[\"00000000-0000-0000-0000-000000000000\"],\"effectiveIds\":[\"00000000-0000-0000-0000-000000000000\"]}"},
  {"id":42,"…":"…"}],
 "nextCursor":"eyJiIjp7InBheW1lbnQiOjQyfSwieCI6W119","unavailableServices":[]}
```

**Paging:**
- Walk the trail by passing `nextCursor` back as `?cursor=` until it is `null`. Observed: the whole trail (493 rows
  across five services) walked at `pageSize` 100 and at 7 gave the same 493 rows, with no row repeated or missing, and
  the per-service counts matched each service's own table.
- The order is newest first by `occurredAt` across services. Within one service, rows come in the order they were
  written, which is nearly always time order; two rows of the same instant are ordered by service name.
- `id` is a number that is **unique only within its service**. Key a row by `service` + `id`.
- `nextCursor` is opaque. Do not build or edit one: anything this endpoint did not issue is 400.

**`payloadJson`** is the command's input, as a **JSON string** (parse it a second time), rendered for the record:
- secrets and personal data are masked as `"****"` (for example a test send's `"email":"****"`);
- a list is cut after 25 items, and the cut is marked by a string item `"[TruncatedAfter:25]"`, even inside a list of
  ids;
- it can contain values the server computed from the input, not only what the caller sent (Payment's `effectiveIds`,
  Catalog's `cacheKeysToInvalidate`).

Show it as formatted text for people. Do not rely on its shape (F-58).

**`unavailableServices`:** the services that could not be read for this page. Their rows are not lost: the cursor keeps
their place, and they appear on a later page, **out of time order**. Observed with Payment paused: the first page
started with Ordering's newest rows, and `unavailableServices` was `["payment"]`.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `pageSize` outside 1–100 (`"'pageSize' must be between 1 and 100."`); an unknown `service` (`"'service' must be one of: catalog, identity, notification, ordering, payment."`); a bad `outcome` (`"'outcome' must be one of: Succeeded, Rejected, Failed."`); `from` not before `to` (`"'from' must be earlier than 'to'."`); a filter over its length (`"'action' must be at most 100 characters."`); a cursor it did not issue (`"'cursor' is not a cursor this endpoint issued."`) |
| 400 | — (empty body) | A value of the wrong type (`pageSize=abc`, `from=yesterday`) (F-20) |
| 401 / 403 | — | Anonymous / no `audit.read` (gateway) |

Note the messages name parameters in quotes and camelCase (`'pageSize'`), unlike the services' `PageSize: …`.

### `GET /api/v1/admin/health`

The health of the gateway and of every service, for the System page. Source: `GatewaySystemEndpoints.ReadHealthAsync`,
`SystemFanOut.GetHealthAsync`.

**Always 200**, whatever the platform's state: the status is in the body. (Each service's own `/health` answers 503
when it is unhealthy, for load balancers; see [conventions.md §12](conventions.md#12-infrastructure-endpoints).)

**200** [`SystemHealth`](#systemhealth). Captured (checks of four services left out):

```json
{"status":"Healthy","checkedAt":"2026-09-25T18:09:42.1310303Z","components":[
  {"name":"gateway","reachable":true,"status":"Healthy","checks":[{"name":"smtp","status":"Healthy"},
    {"name":"email-queue","status":"Healthy"},{"name":"gateway-liveness","status":"Healthy"}]},
  {"name":"identity","reachable":true,"status":"Healthy","checks":[{"name":"outbox","status":"Healthy"},
    {"name":"masstransit-bus","status":"Healthy"},{"name":"rabbitmq","status":"Healthy"},
    {"name":"postgresql","status":"Healthy"},{"name":"redis","status":"Healthy"},
    {"name":"identity-readiness","status":"Healthy"},{"name":"identity-liveness","status":"Healthy"}]},
  {"name":"notification","reachable":true,"status":"Healthy","checks":[{"name":"notification-db","status":"Healthy"},
    {"name":"smtp","status":"Healthy"},{"name":"notification-liveness","status":"Healthy"},
    {"name":"masstransit-bus","status":"Healthy"},{"name":"rabbitmq","status":"Healthy"}]}]}
```

- `components` always lists seven, in this order: `gateway`, `identity`, `catalog`, `basket`, `ordering`, `payment`,
  `notification`.
- `status` is the **worst** component status: `Unhealthy` < `Degraded` < `Healthy`.
- Each component's `checks` are that service's own, and **their names differ per service**. Show them as a list; do
  not hard-code the names.
- A component the gateway could not reach is `{"name":"payment","reachable":false,"status":"Unhealthy","checks":[]}`
  (observed with Payment paused, which made the whole page `Unhealthy`). A service that answered with something other
  than a health report is `reachable: true`, `Unhealthy`, with no checks (from source).
- Observed with the mail server stopped: the gateway's and Notification's `smtp` checks were `Unhealthy`, so both
  components and the page were `Unhealthy`.
- The body carries names and statuses only: no descriptions, errors or addresses.

401 / 403 as above. It has no other error.

### `GET /api/v1/admin/settings`

What governs pricing and payment today, read-only. Source: `GatewaySystemEndpoints.ReadSettingsAsync`; Ordering and
Payment each serve their slice. Changing a setting is a redeploy; there is no write endpoint.

**200** [`SystemSettings`](#systemsettings). Captured:

```json
{"pricing":{"currency":"USD","taxApplied":false,"shippingCharged":false},"payments":{"provider":"Stripe"},
 "unavailableServices":[]}
```

- `pricing` comes from Ordering. `taxApplied` and `shippingCharged` are `false` because neither exists: an order's
  total is the sum of its lines.
- `payments.provider` is `"Stripe"` (customers pay by card) or `"Simulator"` (Stripe is switched off, and every order
  is settled by the payment simulator).
- A slice is `null` when its service could not be read, and the service is named in `unavailableServices`. Observed
  with Payment paused: `"payments":null,"unavailableServices":["payment"]`.

401 / 403 as above.

### `GET /api/v1/admin/feature-flags`

The gateway's fault injection and Payment's simulator, read-only. Source: `GatewaySystemEndpoints.ReadFeatureFlagsAsync`.

**200** [`FeatureFlags`](#featureflags). Captured:

```json
{"gatewaySimulation":{"enabled":false,"allowHeaderOverride":true,"routes":[
   {"routeId":"catalog-products-read-route","pathPrefix":"/api/v1/products","enabled":true,"active":false,
    "errorRate":0.02,"delayMinMs":20,"delayMaxMs":100,"forcedFailureMode":null},
   {"routeId":"orders-route","pathPrefix":"/api/v1/orders","enabled":true,"active":false,
    "errorRate":0.05,"delayMinMs":50,"delayMaxMs":200,"forcedFailureMode":null}]},
 "payments":{"simulatorActive":false,"simulationMode":"Random","successRatePercent":80,
   "processingDelayMinSeconds":1,"processingDelayMaxSeconds":3,"refundDelaySeconds":2,
   "webhookSignatureVerificationSkipped":false},
 "unavailableServices":[]}
```

- `gatewaySimulation` comes from the gateway's own configuration, so it is always present. `enabled` is the master
  switch. A route injects faults only when it is `active`, which means its own `enabled` **and** the master switch are
  on. Injected failures carry `errorCode` `Gateway.SimulatedFailure`
  ([conventions.md §3.4](conventions.md#34-errorcode-values)).
- `payments` comes from Payment, and is `null` when Payment could not be read (observed). `simulatorActive` is `true`
  exactly when Stripe is off. The simulation values are reported either way.
- `webhookSignatureVerificationSkipped: true` is only possible in Development, Sandbox and Testing.

401 / 403 as above.

---

## Types

C# sources: `AuditLogEntryDto` and `AuditLogRequest` (`EShop.BuildingBlocks.Infrastructure/Auditing/`),
`GatewayAuditLogPageDto` (`EShop.ApiGateway/AuditLog/AuditLogFanOut.cs`), `SystemHealthDto`, `SystemSettingsDto`,
`FeatureFlagsDto` and their parts (`EShop.ApiGateway/SystemAdmin/GatewaySystemEndpoints.cs`), and
`PricingSettingsDto`, `PaymentSettingsDto`, `PaymentFeatureFlagsDto`
(`EShop.BuildingBlocks.Infrastructure/SystemAdmin/SystemAdminContracts.cs`). Every timestamp is UTC with `Z`.

### Enums

| Enum | Sent as | Values |
|---|---|---|
| `AuditOutcome` | PascalCase string | `"Succeeded"` · `"Rejected"` · `"Failed"` |
| Audit `service` | lower-case string | `"catalog"` · `"identity"` · `"notification"` · `"ordering"` · `"payment"` |
| `HealthStatus` | PascalCase string | `"Healthy"` · `"Degraded"` · `"Unhealthy"` |
| Payment provider | PascalCase string | `"Stripe"` · `"Simulator"` |
| Simulation mode | PascalCase string | `"Random"` · `"AlwaysSuccess"` · `"AlwaysFailure"` |

### AuditLogQuery

`cursor`, `pageSize` (default 50, 1–100), `service`, `actorUserId`, `action`, `entityType`, `entityId`, `outcome`,
`from` (inclusive), `to` (exclusive); see [the audit trail](#get-apiv1adminaudit).

### AuditLogPage

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `items` | [`AuditLogEntry`](#auditlogentry)[] | no | Newest first |
| `nextCursor` | string | yes | `null` once every service has been read to its end |
| `unavailableServices` | string[] | no | Services not read for this page |

### AuditLogEntry

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `id` | number | no | Unique within its `service` only |
| `occurredAt` | string (date-time) | no | |
| `service` | string | no | Which service recorded it |
| `action` | string | no | The command, without `Command` (`RefundPayment`, `MarkNotificationUndeliverable`) |
| `entityType` | string | no | What it acted on (`Payment`, `Product`, `NotificationTemplate`) |
| `entityId` | string | yes | Which one; `null` for commands that act on no single entity |
| `actorUserId` | string | yes | Who, as an Identity user id |
| `actorName` | string | yes | The actor's email at the time (observed `"admin@eshop.com"`) |
| `correlationId` | string | yes | The request's `X-Correlation-ID`, cut to 100 characters |
| `outcome` | [`AuditOutcome`](#enums) | no | |
| `errorCode` | string | yes | The refusal's code (`Rejected`) or the exception type (`Failed`) |
| `payloadJson` | string | yes | The input as a JSON **string**, masked and cut; see [above](#get-apiv1adminaudit) |

### SystemHealth

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `status` | [`HealthStatus`](#enums) | no | The worst component status |
| `checkedAt` | string (date-time) | no | |
| `components` | `ComponentHealth[]` | no | Seven, in a fixed order |

`ComponentHealth`: `name` (string), `reachable` (boolean), `status` ([`HealthStatus`](#enums)), `checks`
(`{ name: string, status: HealthStatus }[]`, empty when not reachable).

### SystemSettings

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `pricing` | `{ currency, taxApplied, shippingCharged }` | yes | From Ordering. `currency` is always `"USD"` |
| `payments` | `{ provider }` | yes | From Payment |
| `unavailableServices` | string[] | no | `"ordering"` and/or `"payment"` |

### FeatureFlags

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `gatewaySimulation` | `{ enabled, allowHeaderOverride, routes }` | no | From the gateway |
| `payments` | `PaymentFeatureFlags` | yes | From Payment |
| `unavailableServices` | string[] | no | `"payment"` or empty |

A simulation route: `routeId`, `pathPrefix` (strings), `enabled`, `active` (booleans), `errorRate` (number, 0–1),
`delayMinMs`, `delayMaxMs` (numbers), `forcedFailureMode` (string or `null`).

### TypeScript

`ProblemDetails` is in [conventions.md](conventions.md).

```ts
// ---- Enums (sent as strings) ----

export type AuditOutcome = 'Succeeded' | 'Rejected' | 'Failed';
/** The services whose audit trail the gateway merges. Basket keeps none. */
export type AuditService = 'catalog' | 'identity' | 'notification' | 'ordering' | 'payment';
export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';
/** The health page's components, in the order they are listed. */
export type SystemComponent =
  | 'gateway'
  | 'identity'
  | 'catalog'
  | 'basket'
  | 'ordering'
  | 'payment'
  | 'notification';
export type PaymentProvider = 'Stripe' | 'Simulator';
export type PaymentSimulationMode = 'Random' | 'AlwaysSuccess' | 'AlwaysFailure';

// ---- Audit trail ----

export interface AuditLogQuery {
  /** The previous page's nextCursor. Keep every other parameter unchanged while paging. */
  cursor?: string;
  /** One service only. */
  service?: AuditService;
  /** Default 50, 1-100. */
  pageSize?: number;
  /** Exact, case-sensitive; <= 200 chars. */
  actorUserId?: string;
  /** Exact, case-sensitive command name without "Command", e.g. RefundPayment; <= 100 chars. */
  action?: string;
  /** Exact, case-sensitive, e.g. Product; <= 100 chars. */
  entityType?: string;
  /** Exact, case-sensitive; <= 200 chars. */
  entityId?: string;
  /** Any case. */
  outcome?: AuditOutcome;
  /** ISO instant; inclusive. */
  from?: string;
  /** ISO instant; EXCLUSIVE, and must be later than from. */
  to?: string;
}

export interface AuditLogEntry {
  /** Unique only within its service: key rows by service + id. */
  id: number;
  occurredAt: string;
  service: AuditService;
  action: string;
  entityType: string;
  entityId: string | null;
  actorUserId: string | null;
  /** The actor's email at the time. */
  actorName: string | null;
  correlationId: string | null;
  outcome: AuditOutcome;
  /** The refusal's errorCode (Rejected) or the exception type name (Failed). */
  errorCode: string | null;
  /** The input as a JSON string: masked ("****"), lists cut at 25. Parse it again; do not rely on its shape. */
  payloadJson: string | null;
}

export interface AuditLogPage {
  /** Newest first, across services. */
  items: AuditLogEntry[];
  /** null once every service has been read to its end. */
  nextCursor: string | null;
  /** Services not read for this page; their rows come on a later page. */
  unavailableServices: AuditService[];
}

// ---- System health ----

export interface HealthCheckEntry {
  /** Names differ per service; do not hard-code them. */
  name: string;
  status: HealthStatus;
}

export interface ComponentHealth {
  name: SystemComponent;
  /** false: no answer within 5 s (status is then Unhealthy, checks empty). */
  reachable: boolean;
  status: HealthStatus;
  checks: HealthCheckEntry[];
}

export interface SystemHealth {
  /** The worst component status. */
  status: HealthStatus;
  checkedAt: string;
  components: ComponentHealth[];
}

// ---- Settings ----

export interface PricingSettings {
  /** Always "USD". */
  currency: string;
  /** false: no tax is applied. */
  taxApplied: boolean;
  /** false: no shipping cost is charged. */
  shippingCharged: boolean;
}

export interface PaymentSettings {
  provider: PaymentProvider;
}

export interface SystemSettings {
  /** null when Ordering could not be read. */
  pricing: PricingSettings | null;
  /** null when Payment could not be read. */
  payments: PaymentSettings | null;
  unavailableServices: ('ordering' | 'payment')[];
}

// ---- Feature flags ----

export interface SimulationRouteFlag {
  routeId: string;
  pathPrefix: string;
  /** The route's own switch. */
  enabled: boolean;
  /** Injecting faults now: enabled AND the master switch. */
  active: boolean;
  /** 0-1. */
  errorRate: number;
  delayMinMs: number;
  delayMaxMs: number;
  forcedFailureMode: string | null;
}

export interface GatewaySimulationFlags {
  /** The master switch. */
  enabled: boolean;
  allowHeaderOverride: boolean;
  routes: SimulationRouteFlag[];
}

export interface PaymentFeatureFlags {
  /** true exactly when Stripe is off. */
  simulatorActive: boolean;
  simulationMode: PaymentSimulationMode;
  successRatePercent: number;
  processingDelayMinSeconds: number;
  processingDelayMaxSeconds: number;
  refundDelaySeconds: number;
  webhookSignatureVerificationSkipped: boolean;
}

export interface FeatureFlags {
  gatewaySimulation: GatewaySimulationFlags;
  /** null when Payment could not be read. */
  payments: PaymentFeatureFlags | null;
  unavailableServices: 'payment'[];
}
```

---

## Frontend notes

> ⚠ **Paging the audit trail never ends while a service is unavailable.** `nextCursor` stays non-null until every
> service has been read to its end, and an unavailable service is never read. Observed with Payment paused and
> `?service=payment`: `items: []`, `unavailableServices: ["payment"]`, and a non-null `nextCursor` that starts again
> from the top. Stop paging when a page is empty, and offer "retry" when `unavailableServices` is not empty. (F-57)

> ⚠ **`payloadJson` is a string, and not exactly the request.** Parse it a second time. Values are masked (`"****"`),
> lists are cut at 25 with a `"[TruncatedAfter:25]"` string item even in a list of ids, and server-computed members
> appear beside the caller's. Render it as text. (F-58)

> ⚠ **`Rejected` does not mean nothing changed**: a refused command may already have written something. For the
> actual state, read the entity itself.

> ⚠ **Audit `id`s repeat across services.** Use `service` + `id` as the React key.

> ⚠ **These pages can take 5 s** when one service hangs, because the gateway waits up to 5 s for each service. Show a
> loading state rather than a spinner that times out sooner.

> ⚠ **A query value of the wrong type gets a bare 400 with no body** on the audit endpoint (`pageSize=abc`,
> `from=yesterday`). Validate on the client. (F-20)

> ⚠ **The OpenAPI document promises problem+json bodies on 401 and 403** for all four endpoints; the gateway sends
> none. (F-11)

---

## Related documents

- [conventions.md](conventions.md): errors, auth, permissions, the audit page shape (§6.5)
- [notification.md](notification.md), [payment.md](payment.md), [ordering.md](ordering.md), [catalog.md](catalog.md),
  [identity.md](identity.md): the audited actions of each service
- [flows.md](flows.md): admin flows

---

**Version**: 1.0  
**Last Updated**: 2026-09-25
