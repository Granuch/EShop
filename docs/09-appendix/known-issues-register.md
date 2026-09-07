# Known Issues Register

Single consolidated list of every known bug, security issue, and piece of code debt in
this repository, with each item re-verified against the source tree rather than carried
forward on trust.

**Compiled:** 2026-09-07 · **Tree:** branch `bug/bug-fix`, clean · **Method:** static
verification of every claim against current source; runtime results reused only where an
earlier pass recorded them explicitly.

## How to read this

| Status | Meaning |
|---|---|
| **OPEN** | Re-verified against current source. The problem is present now. |
| **FIXED** | Was real, has since been fixed. Listed so it is not re-reported. |
| **REFUTED** | Was reported but is not true — verified against the code/runtime. Do not act on it. |
| **STALE** | The claim was true once; the code has moved and the description no longer matches. |

Severity is assigned from the verified behaviour, not from the original report — where the
two differ the change is called out in the entry.

- **Critical** — breaks correctness or security in a reachable way.
- **High** — real defect with material impact, or a security weakening.
- **Medium** — wrong behaviour under specific conditions, or debt that actively misleads.
- **Low** — cosmetic, dead, or style-level.

Every OPEN item below carries a `file:line` that was read during compilation. Line numbers
drift; re-anchor on the symbol name.

---

## Verification baseline

Everything in this register was compiled against a **green** tree, so none of it is a
symptom of a broken build. Recorded so a future reader can tell "known issue" from
"something I just broke":

| Suite | Result |
|---|---|
| `dotnet test EShop.slnx -c Release` — 13 projects | **776 passed / 0 failed** |
| `tests/ApiGateways/EShop.ApiGateway.IntegrationTests` — not in the slnx | **5 passed / 1 failed** |

The single gateway failure is `ReadinessEndpoint_ShouldReturnHealthyOrDegradedInTesting`
(`Expected: OK, But was: ServiceUnavailable`) — pre-existing and environment-driven, see
TEST-02. Apart from that one, **no defect in this register is caught by any test** — every
suite is green while all 87 open items are present. That gap is itself the subject of
TEST-01 through TEST-06.

## Summary

| Severity | Open |
|---|---|
| Critical | 3 |
| High | 15 |
| Medium | 33 |
| Low | 36 |
| **Total open** | **87** |

Plus **2** previously-real defects now fixed (kept so they are not re-reported) and **6**
refuted or stale claims (kept so they are not re-derived).

The five highest-value items to fix first, by impact-per-effort — all five are a handful of
lines each:

1. **BUG-01** — one word (`throw;`) turns every silent startup failure into a diagnosable one.
2. **BUG-02** — one line (`ValueGeneratedNever()`) fixes a reachable 409 on `POST /orders/{id}/items`.
3. **SEC-02** — two method calls close a ~65-minute privilege-retention window after role revocation.
4. **SEC-03** — partitioning Catalog's `"search"` limiter stops one client locking out all others.
5. **BUG-06** — moving one marker interface fixes a 5-minute stale 2FA state.

The two structural items that cause the most *hidden* risk are **OPS-01** (nothing compiles
or tests the solution on a PR) and **TEST-01** (no test anywhere runs against a real
Postgres). Between them they are the reason several entries below could sit undetected.

---

## Critical

### BUG-01 · Identity startup failures exit with code 0 · OPEN
*Origin: audit C-1 · runtime-verified*

`src/Services/Identity/EShop.Identity.API/Program.cs:671-677` — the top-level
`catch (Exception ex) { Log.Fatal(...); } finally { Log.CloseAndFlush(); }` has no `throw`
and sets no exit code. Any startup exception (config-guard rejection, unreachable RabbitMQ)
logs `[FTL]` and then reports **success**.

Note the *inner* migration catch at `:529-533` does rethrow — only the outer one swallows.

**Impact.** The exit code lies to every wrapper that checks status instead of parsing logs.
Both Docker (`restart: unless-stopped`) and Kubernetes (`restartPolicy: Always`) *do* still
restart the container, so the service does not silently vanish — but the restart loop
reports `exitcode=0`, which reads as "completed normally" rather than "rejected its config".

> The original report claimed Kubernetes treats exit 0 as a clean shutdown. That is
> **wrong** and was disproved in-cluster; a Deployment pod goes to CrashLoopBackOff. The
> finding stands, its blast radius is narrower than first written.

**Fix:** `throw;` after `Log.Fatal`.

### BUG-02 · `POST /orders/{id}/items` fails with 409 on Postgres · OPEN
*Newly identified during this compilation — promoted from a latent repo-wide note to a live bug*

`OrderItem`'s constructor assigns its own key
(`src/Services/Ordering/EShop.Ordering.Domain/Entities/OrderItem.cs:28`), but
`OrderingDbContext` maps that key with a bare `entity.HasKey(i => i.Id)` and **no
`ValueGeneratedNever()`**
(`src/Services/Ordering/EShop.Ordering.Infrastructure/Data/OrderingDbContext.cs:93-97`).

Left at the default `ValueGeneratedOnAdd`, EF sees an already-set store-generated key on a
child discovered under a non-`Added` parent, concludes the row exists, and issues an
`UPDATE` that matches nothing → `DbUpdateConcurrencyException` → 409.

**This path is reachable, not theoretical.** `AddOrderItemCommandHandler:33-46` loads an
existing order and calls `order.AddItem(...)`; `OrderRepository.UpdateAsync:64-72` attaches
and calls `DetectChanges()`. The endpoint is `OrderEndpoints.cs:91`.

Catalog fixed exactly this for both of its children
(`CatalogDbContext.cs:150-151` and `:184-185`, with the reasoning in a comment). Ordering
was never brought along.

**Why it has survived:** every integration project in this repo runs on EF InMemory, and
`AddOrderItem` has only a unit test with a mocked repository
(`tests/Services/Ordering/EShop.Ordering.UnitTests/Orders/AddOrderItemCommandHandlerTests.cs`).
No test touches a real provider on this path.

**Fix:** add `entity.Property(i => i.Id).ValueGeneratedNever();` to the `OrderItem` block.

### BUG-03 · Registration issues accounts that can never log in · OPEN (Production only)
*Origin: audit C-3*

`RegisterCommandHandler.cs:91` generates an email-confirmation token and never references it
again — it is on neither `RegisterResponse` nor `UserRegisteredIntegrationEvent`, so
Notification has nothing to put in the email. The response still says
*"Registration successful. Please check your email to confirm."*

`SignIn.RequireConfirmedEmail` is false in Development and Sandbox
(`Infrastructure/Extensions/ServiceCollectionExtensions.cs:86`), which is the only reason
this has not bitten. The first Production deploy flips it true and every newly registered
account is permanently locked out with no way to mint a token.

**Owner note:** email confirmation and 2FA are deliberately parked scaffolding, not
oversights. This entry records the *latent production trap*, not a demand to finish the
feature. Wire the token into the integration event before any Production deploy, or gate
registration off loudly where `RequireConfirmedEmail` is true.

### ~~BUG-04~~ · Identity's Result path and exception path returned different error shapes · FIXED
*Origin: audit API-2 / API-3*

Identity returned anonymous `{ error, message }` from `Result` failures and RFC 7807 from the
exception path. Both now emit one canonical envelope via
`Controllers/ApiControllerBase.ProblemForError` and the shared
`BuildingBlocks.Infrastructure/Http/ProblemDetailsExceptionMiddleware`. The six copy-pasted
per-service middlewares were deleted. See DEBT-17 for the one component still inconsistent.

### ~~BUG-05~~ · Identity's `"auth"`/`"login"` limiters were one global bucket · FIXED
*Origin: audit C-2*

`AddFixedWindowLimiter(policyName, …)` has no partition key. Identity now uses
`AddPolicy<string>` keyed on the client IP (`Program.cs:391-415`). **Two unpartitioned
policies remain elsewhere — see SEC-03.**

---

## High

### SEC-01 · `X-Forwarded-For` is ignored by 6 of 7 HTTP components · OPEN
*Origin: audit H-6, partially fixed*

| Component | Reads | Effective behaviour |
|---|---|---|
| Identity | `KnownProxies` **and** `KnownNetworks` (`Program.cs:70-123`) | ✅ works under Docker/k8s |
| Gateway | `KnownProxies` only (`Program.cs:56-72`) | ❌ |
| Basket | `KnownProxies` only (`Program.cs:51-67`) | ❌ |
| Catalog | `KnownProxies` only (`Program.cs:67-83`) | ❌ |
| Ordering | `KnownProxies` only (`Program.cs:58-74`) | ❌ |
| Payment | **no forwarded-headers handling at all** | ❌ |
| Notification | **no forwarded-headers handling at all** | ❌ (no HTTP surface, so harmless) |

`KnownProxies` needs a fixed proxy address, which does not exist under Docker or Kubernetes
where the caller's address comes from a bridge or pod CIDR. So five components silently
ignore `X-Forwarded-For`, and every IP-derived control — rate-limit partitions, brute-force
tracking, `LastLoginIp` — sees the gateway rather than the real client.

**Two configuration consequences to be aware of:** `k8s/02-configmap.yaml` sets
`ForwardedHeaders__KnownNetworks__0-2` namespace-wide and Identity's
`appsettings.Sandbox.json` sets the same CIDRs — **both are no-ops everywhere except
Identity.** And `appsettings.Production.json` ships an unsubstituted
`"KnownProxies": [ "#{TRUSTED_PROXY_IP}#" ]` (Identity `:68`, Basket `:48`), which disables
forwarded headers entirely; Identity logs an error on the unparseable entry, the others drop
it silently.

**Fix:** port Identity's parser block (`Program.cs:63-135`) into the four siblings.

### SEC-02 · Role changes never invalidate the roles cache · OPEN
*Origin: audit H-2*

`InvalidateRolesCacheAsync` has **no production caller** — grep returns only its declaration
(`CachedUserRolesService.cs:35`), its implementation (`:110`), and one unit test. Tested but
unused: coverage that implies it works.

`RolesController.AddUserToRole` / `RemoveUserFromRole` mutate `user_roles` and return 204
without touching `user_roles:{userId}`, which has a 5-minute absolute TTL.

**Exploit path on revocation:** remove a user from `Admin` → the cache still returns
`["Admin"]` for up to 5 min → any token minted in that window carries the Admin role → that
token lives its full 3600s lifetime. Worst case **≈65 minutes of retained privilege after
revocation**.

**Fix:** call `InvalidateRolesCacheAsync` on both membership mutations. Longer term, move
role management behind commands so it inherits the pipeline (see DEBT-01).

### SEC-03 · Two rate-limit policies are still one global bucket · OPEN
*Origin: audit C-2, residue after the Identity fix*

`options.AddFixedWindowLimiter(policyName, …)` builds **one limiter shared by every caller** —
it has no partition key. Two remain:

- **Catalog `"search"`** (`Program.cs:278`) — caps `GET /api/v1/products` at 30/min for the
  *whole service*. One client can lock out every other. Attached at `ProductEndpoints.cs:55`.
- **Gateway `"simulation"`** (`Program.cs:158`).

This is the repo's highest-consequence ASP.NET footgun because it sits directly beside a
correctly-partitioned `GlobalLimiter` and therefore reads as intentional.

**Invisible to the test suites** — both set the limits to `int.MaxValue` under
`ASPNETCORE_ENVIRONMENT=Testing`.

**Fix:** `options.AddPolicy<string>("name", ctx => RateLimitPartition.GetFixedWindowLimiter(
partitionKey: <client ip>, factory: …))`, as Identity now does. Test it by overriding limits
as *host* configuration and faking the peer address — see Identity's `RateLimitingApiFactory`.
Note SEC-01 must be fixed for Catalog first, or the partition key is the gateway's IP.

### SEC-04 · Refresh tokens are stored in plaintext · OPEN
*Origin: audit H-8.1*

`TokenService.cs:98-101` generates 64 CSPRNG bytes → `Convert.ToBase64String` → stored raw in
`refresh_tokens.Token` (unique index). Any read of that table — backup, log, support query,
SQL injection elsewhere — grants full session takeover for every active session.

The posture is internally contradictory: `RevokedTokenCache` SHA-256-hashes **the same value**
before using it as a cache key, commenting *"We don't store the actual token in the key for
security."* The cache is hardened; the durable store is not.

**Fix:** store a SHA-256 hash and look up by hash (the unique index moves to the hash column —
schema change + migration).

### SEC-05 · Password-reset token persisted in the outbox for 7 days · OPEN
*Origin: audit H-8.2*

`ForgotPasswordCommandHandler.cs:57-62` enqueues `PasswordResetRequestedIntegrationEvent` with
`ResetToken` in the payload, persisted as plaintext JSON in `outbox_messages.Payload` and
retained 7 days by `OutboxCleanupService` — long after the token itself expires.

**Fix:** shorten retention for secret-bearing events, or have Notification fetch the token
rather than carrying it.

### SEC-06 · Password change and reset leave old sessions alive on failure · OPEN
*Origin: audit H-10*

`ChangePasswordCommandHandler.cs:60-68` and `ResetPasswordCommandHandler.cs:72-80` both wrap
`RevokeAllUserTokensAsync` in a `try/catch` that logs and continues, commented
*"Don't fail the operation - password was changed successfully"*.

For a credential-rotation flow that is fail-**open**, not fail-safe. The user believes
changing their password evicted the attacker; every stolen refresh token stays valid for its
full lifetime and the API returns 200 with `"Password changed successfully"`.

Both commands already implement `ITransactionalCommand`, so the revoke could participate in
the ambient transaction — the manual `SaveChangesAsync` inside the behavior's transaction is
what makes the current split fragile.

### SEC-07 · Health endpoints leak infrastructure detail anonymously · OPEN — scope wider than reported
*Origin: audit H-5*

`/health` and `/health/ready` are unauthenticated and use
`UIResponseWriter.WriteHealthCheckUIResponse`, which serializes **per-check descriptions and
exception messages**. `IdentityReadinessHealthCheck` explicitly returns `{ "error", ex.Message }`
in its data dictionary (`IdentityHealthChecks.cs:65`), and the Npgsql check surfaces raw
connection errors — database hostnames, usernames, and failure detail.

> The original report scoped this to Identity. **It is repo-wide:** all six services *and*
> the gateway use the UI writer on both endpoints — Gateway `:259,265`, Basket `:237,243`,
> Catalog `:494,500`, Identity `:621,627`, Notification `:123,129`, Ordering `:403,409`,
> Payment `:234,240`.

`/health/live` already uses a minimal custom writer (Identity `Program.cs:630-639`) — that is
the correct pattern the other two should match.

Same family of exposure in Identity: `/prometheus` and `/metrics` are anonymous and
unfiltered, and OpenAPI + Scalar are served in Production (gated only on
`!IsEnvironment("Testing")`), publishing the full admin `RolesController` surface. The code
comment says the latter is deliberate — confirm intent before changing it.

### SEC-08 · Shipped defaults trip the placeholder guards in Sandbox · OPEN
*Origin: audit H-7 · runtime-verified*

Three independent sources inject the same placeholder, and `Sandbox` is neither Development
nor Testing so the guard at `Identity/Program.cs:111-128` is live:

1. `.env.example:156` — `INTERNAL_SERVICE_API_KEY=LOCAL_internal_service_api_key`
2. `docker-compose.yml:231,317,777` — same value as the compose default
3. `appsettings.Sandbox.json:49` — `"ApiKey": "LOCAL_internal_service_api_key"`

`LOCAL_` is in `placeholderPatterns`, so Identity crash-loops with
`InternalServiceAuth:ApiKey contains placeholder pattern 'LOCAL_' in Sandbox` — and then
exits 0 (BUG-01). Payment and Catalog carry the same pattern list.

**Deleting `.env` does not clear this** — compose supplies the same default and the tracked
appsettings hardcodes it. Export a real value.

**Related, same family:** `appsettings.Production.json` sets
`"AllowedOrigins": [ "https://your-production-frontend.com" ]` (Identity `:65`, Basket `:51`) —
a placeholder that **passes** the empty-array guard, so the guard silently approves a
misconfigured production deploy. `your-production-frontend` is not in `placeholderPatterns`,
and CORS is not checked against that list at all.

### BUG-06 · 2FA enablement is not reflected for 5 minutes · OPEN
*Origin: audit H-1*

`Verify2FACommand.cs:10` declares only `ITransactionalCommand` — no `ICacheInvalidatingCommand`.
The markers are on exactly the wrong commands:

| Command | Flips `TwoFactorEnabled`? | Has `ICacheInvalidatingCommand`? |
|---|---|---|
| `Enable2FACommand` | No (returns shared key + QR only) | **Yes** (unnecessary) |
| `Verify2FACommand` | **Yes** (`SetTwoFactorEnabledAsync`) | **No** ← the bug |
| `Disable2FACommand` | Yes | Yes |

`GetProfileQuery` caches `TwoFactorEnabled` at `identity:v1:profile:{userId}` for 5 minutes
absolute, so `GET /account/profile` keeps reporting `twoFactorEnabled: false` after the user
has successfully enabled 2FA. `GetProfileQuery.cs:11`'s doc comment lists the invalidating
commands and omits `Verify2FA` — consistently wrong in both places.

**Fix:** move the marker onto `Verify2FACommand`; drop it from `Enable2FA`; correct the doc.

### BUG-07 · Nested transaction in `ConfirmEmailCommandHandler` · OPEN
*Origin: audit H-11*

`ConfirmEmailCommand.cs:10` implements `ITransactionalCommand`, so `TransactionBehavior` has
already opened a transaction before the handler runs. The handler then opens its own at
`ConfirmEmailCommandHandler.cs:66` and commits at `:88`.

`TransactionBehavior` guards on `HasActiveTransaction`; **the handler does not**. The inner
commit closes the transaction the behavior still believes it owns, and the behavior's
subsequent commit/rollback operates on nothing.

It is the only handler that both takes the marker interface and drives the unit of work by
hand. **Fix:** pick one — drop the manual transaction, or drop the marker.

### BUG-08 · `RolesController.GetRoles` returns an unmaterialized `IQueryable` · OPEN
*Origin: audit H-9*

`RolesController.cs:35-45` — non-async, no `ToListAsync`, no `CancellationToken`. The JSON
serializer enumerates the query **synchronously during response writing**: a blocking DB call
on the response path, and thread-pool starvation under load.

The compounding failure the original report described has been **partly defused**: the shared
`ProblemDetailsExceptionMiddleware` now checks `Response.HasStarted` (`:72`), so a throw
during serialization no longer destroys the original exception. The blocking enumeration
itself is unchanged.

Also unpaginated — as is `GetUsersInRole`, which loads every user in the role.

### PERF-01 · No Identity controller forwards a `CancellationToken` · OPEN
*Origin: audit H-4*

`grep -c CancellationToken` across all five files in
`src/Services/Identity/EShop.Identity.API/Controllers/` returns **0**. Every dispatch site
uses `_mediator.Send(command)`, which supplies `CancellationToken.None`.

The irony is that everything downstream is correctly plumbed — `ValidationBehavior`,
`TransactionBehavior`, both caching behaviors, `LoginCommandHandler`,
`RefreshTokenCommandHandler` all thread the token properly. The chain is severed at the one
layer that owns `HttpContext.RequestAborted`.

Client disconnects never abort DB queries, Redis round-trips, or brute-force tracking;
abandoned requests hold connection-pool slots for their full natural duration.

**Fix:** mechanical — add `CancellationToken ct` to each action signature and pass it through.
Note six handlers legitimately cannot use it (they are `UserManager`-only, and ASP.NET
Identity's API takes no token).

### OPS-01 · No CI build or test gate · OPEN
*Process issue — highest-leverage item in this table*

The `CI` workflow (`.github/workflows/ci.yml`) is the only one that runs `dotnet build` /
`dotnet test`, and it is **disabled** (`gh workflow list --all` reports `disabled_manually`).
Even enabled, its trigger is `pull_request: branches: [main, develop]` (`:4-5`) — this repo's
default branch is `master`, and `main`/`develop` do not exist on origin, so it would never
fire.

PRs into `master` are gated only by Gateway Quality Gates, Docker Smoke, and SonarCloud, none
of which compile the solution. **Build and test locally before pushing.**

### OPS-02 · Five of six services have no liveness probe · OPEN — *inverts the original report*
*Origin: audit H-12, which claimed the opposite*

> **The original finding was wrong.** It asserted Identity's Deployment declares no probes at
> all. Identity in fact declares **both** a `readinessProbe` (`/health/ready`) and a
> `livenessProbe` (`/health/live`) — `k8s/services/microservices.yaml:80-92`. Marked `[RV]`
> in the original, but the file has one commit and has not changed since.

The real gap is the inverse. Probe inventory across `k8s/`:

| Deployment | readiness | liveness | startup |
|---|---|---|---|
| identity-api | ✅ `:80` | ✅ `:87` | ❌ |
| api-gateway | ✅ | ✅ (`api-gateway.yaml:62`) | ❌ |
| catalog-api | ✅ `:179` | ❌ | ❌ |
| basket-api | ✅ `:267` | ❌ | ❌ |
| ordering-api | ✅ `:360` | ❌ | ❌ |
| payment-api | ✅ `:448` | ❌ | ❌ |
| notification-api | ✅ `:531` | ❌ | ❌ |

So for five of the seven, a **hung-but-alive** process (deadlock, exhausted thread pool,
wedged broker connection) is never restarted — only an outright process exit is caught. And
**no deployment anywhere declares a `startupProbe`**, so a slow migration retry loop competes
with the readiness `initialDelaySeconds` rather than being covered by a dedicated budget.

### DEBT-01 · `RolesController` has no CQRS layer · OPEN
*Origin: audit H-2 compounding note*

`RolesController` calls `_roleManager` / `_userManager` inline across all its actions. Role
mutations therefore bypass `ValidationBehavior`, `LoggingBehavior`, `TransactionBehavior`
**and** `CacheInvalidationBehavior` — every other write in the service goes through all four.
This is the structural reason SEC-02 exists and the reason its 13 failure sites are hardcoded
literals rather than `Result` unwraps.

---

## Medium

### DEBT-02 · Gateway emits two error shapes · OPEN
Four proxy-guard middlewares still write ad-hoc JSON directly:
`{Basket,Catalog,Identity,Ordering}ProxyGuardMiddleware`, each at its `WriteAsJsonAsync`
call (`:32`, `:36`, `:37`, `:36` respectively), emitting
`new { error = "Request.PayloadTooLarge", message = ... }`. `SimulationResponseFactory` and
the `/health/live` writer also write their own `application/json` bodies.

The gateway's *exception* path was migrated to the canonical RFC 7807 envelope, so a
payload-too-large rejection and an unhandled gateway exception now answer in different shapes.
`grep -rn 'error = "' --include=*.cs src/` returns **only** these four files — they are the
last of the old shape in the repo.

Each has a dedicated test in `tests/ApiGateways/EShop.ApiGateway.UnitTests/Middleware/`, so
the migration is covered. It was left out of the error-envelope unification only because it
was outside that task's stated scope.

### TEST-01 · Every integration project runs on EF InMemory · OPEN
No integration project in this repo uses a real Postgres. Column length limits, unique and
filtered indexes, cascade deletes, and provider-specific query translation are unexercised
repo-wide. This single decision is what hides BUG-02, DEBT-06, DEBT-07 and DEBT-15.

`Testcontainers.PostgreSql` 4.4.0 is a `PackageReference` in
`tests/Services/Identity/EShop.Identity.IntegrationTests/*.csproj:23` and is used by **zero
`.cs` files**. One Testcontainers-backed smoke test would retire several entries here at once.

### TEST-02 · `EShop.slnx` omits one existing test project · OPEN
The solution registers **13** test projects; **14** exist on disk.
`tests/ApiGateways/EShop.ApiGateway.IntegrationTests` (6 tests) is missing, so
`dotnet test EShop.slnx` silently skips it and reports success. Run it explicitly.

Expect **5/6** — `ReadinessEndpoint_ShouldReturnHealthyOrDegradedInTesting` fails with
`Expected: OK, But was: ServiceUnavailable` on a clean tree. `/health/ready` aggregates three
`ready`-tagged checks that reach for real hosts the in-process test host does not have, so the
endpoint returns 503 despite the test's name allowing "Degraded". Pre-existing and
environment-driven.

### TEST-03 · No BuildingBlocks test project · OPEN
`EShop.slnx` registers the four `src/BuildingBlocks/*` projects but `tests/` has only
`ApiGateways/`, `Load/` and `Services/`. Nothing unit-tests `ValidationBehavior`,
`TransactionBehavior`, `CachingBehavior`, the outbox, `IdempotentConsumer`, or the shared
problem-details primitives directly.

A change there is validated only by whichever service integration suite happens to exercise
it, and a regression surfaces as a failure in an apparently unrelated service. Run the whole
suite when touching BuildingBlocks.

### TEST-04 · Six zero-byte test files imply coverage that does not exist · OPEN
All under `tests/Services/Identity/EShop.Identity.IntegrationTests/`:
`Auth/ForgotPasswordTests.cs`, `Auth/RefreshTokenTests.cs`, `Auth/RevokeTokenTests.cs`,
`Roles/RolesTests.cs`, `Validation/ValidationIntegrationTests.cs`,
`Infrastructure/TestHelpers.cs`.

### TEST-05 · Several Identity tests cannot fail · OPEN
```csharp
// SecurityTests.cs:242 — passes either way
response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
// SecurityTests.cs:172 — a bad-password login can only be 400 or 401; a real SQLi regression passes
response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
// ConcurrentOperationTests.cs:99-100 — passes with 4 successful concurrent password changes,
// which is exactly the bug it claims to guard
successCount.Should().BeGreaterThanOrEqualTo(1); failureCount.Should().BeGreaterThanOrEqualTo(1);
```
Plus: `LoginAttemptTrackerTests` verifies only that mocks it scripted were called;
`ServiceCollectionExtensionsTests` asserts a DI registration *count*; `MetricsTests` asserts
prometheus-net's own `"# HELP"` output; `Response_ShouldNotExposeServerInfo` is vacuous under
`WebApplicationFactory` (TestServer never emits a `Server` header); wall-clock perf thresholds
under InMemory measure nothing and are flake sources.

### TEST-06 · Identity coverage gaps · OPEN
- **9 of 15 handlers have no unit test:** `Login`, `Register`, `ResetPassword`,
  `ChangePassword`, `UpdateProfile`, `Enable2FA`, `Disable2FA`, `GetProfile`, `GetUserContact`.
- **2 of 15 validators tested.**
- **Domain layer: zero tests** — including `IdentifierHasher`, which produces every log
  correlation ID and cache key.
- **Endpoints never called by any test:** `POST /auth/confirm-email`, `GET /roles/{id}`,
  `PUT /roles/{id}`, both `/roles/{roleName}/users/{userId}` verbs, `GET /users/{userId}/contact`
  — so the entire `InternalService` API-key authorization policy is untested.
- No happy-path registration test, no valid-token reset-password success test, **no outbox
  assertion anywhere** despite `UseOutbox => true`.

### TEST-07 · Dead test scaffolding · OPEN
`TransactionalIntegrationTestBase` (zero subclasses, and a no-op on InMemory anyway);
`Infrastructure/SharedDatabaseFixture.SharedFactory` (a `[SetUpFixture]` booting a second host
for the whole assembly that nothing reads); a dangling `<Folder Include="Caching\" />` in the
csproj.

### PERF-02 · Identity integration suite takes ~3m15s for 123 tests · OPEN
`IntegrationTestBase` builds a fresh `WebApplicationFactory` per test method — a full host boot
per test — and `UniformResponseTimingMiddleware` pads every request by 800–1200 ms. It will
blow a default 120s command timeout; allow 600s.

### DEBT-03 · Identity middleware pipeline order · OPEN
*Origin: audit API-5*

`Program.cs` orders `UseRateLimiter` (`:588`) → `UseCors` (`:591`) → `UseHttpsRedirection`
(`:596`). A plaintext request therefore consumes a rate-limit permit and is CORS-processed
*before* being redirected.

It is also conditional on `ASPNETCORE_HTTPS_PORT`/`HTTPS_PORT`, which `docker-compose.yml`
never sets — so **under compose there is no HTTPS redirect and no HSTS at all**.

Correct order: `UseForwardedHeaders → UseHttpsRedirection → UseRouting → UseCors →
UseRateLimiter → UseAuthentication → UseAuthorization`.

### DEBT-04 · `UniformResponseTimingMiddleware` sleeps without a token · OPEN
*Origin: audit API-4*

`await Task.Delay(delayMs);` at `:84` — no `context.RequestAborted`. On disconnect the request
still occupies a thread for up to 1.2s; combined with the global limiter that is a cheap
resource-exhaustion lever.

The padding also runs in a `finally` **after** inner middleware may already have written the
body, weakening the anti-enumeration guarantee it exists to provide. It covers only
`/api/v1/auth/{login,register,forgot-password,reset-password}` — **not** `confirm-email`
(which reveals whether a `UserId` exists) or `refresh-token`.

### DEBT-05 · Zero `AsNoTracking()` in Identity · OPEN
*Origin: audit I-1 · grep-confirmed across both projects*

`RefreshTokenRepository.GetByTokenAsync` is tracked *and* `.Include(t => t.User)` — the full
user graph loads on every token refresh. `GetActiveTokensByUserIdAsync` is tracked and
**unbounded** (no `Take`).

### DEBT-06 · Repository queries fork on provider — the shipped branch is the untested one · OPEN
*Origin: audit I-2*

`RefreshTokenRepository.cs:50` and `:85` branch on `_context.Database.IsInMemory()`:
`ExecuteUpdateAsync` on Postgres (`:69`, `:113`), a tracked loop on InMemory. **Production
takes the first; every test takes the second.** They differ semantically —
`ExecuteUpdateAsync` bypasses the `Version` (xmin) concurrency check, `SaveChangesAsync`,
`SetAuditFields()`, and outbox dispatch.

### DEBT-07 · `TokenCleanupService` materializes all expired tokens · OPEN
*Origin: audit I-3*

`ToListAsync` (`:51`) + `RemoveRange` (`:55`), with an explicit comment at `:46-47`:
*"ExecuteDeleteAsync is more efficient for real databases (PostgreSQL), but we use ToListAsync
+ RemoveRange for compatibility with InMemory provider in tests"*. Test-provider compatibility
is dictating a production query shape; with 30-day retention this is an unbounded
materialization on a background thread, unbatched, no `Take`.

### DEBT-08 · `LoginAttemptTracker`'s `IDistributedCache` fallback is non-atomic · OPEN
*Origin: audit I-4*

The Redis path uses `StringIncrementAsync` (`:339`); the fallback is read-modify-write on a
JSON-serialized `HashSet` (`:387`). Concurrent failed logins lose increments — weakening
lockout exactly under attack.

### DEBT-09 · Redis key namespaces diverge between the two cache paths · OPEN
*Origin: audit I-5*

`Program.cs:222` sets `InstanceName = "EShop_Identity_"` for `IDistributedCache`; the raw
multiplexer path uses `bf_protection` (`BruteForceProtectionSettings.cs:96`) with no instance
prefix. The two paths address **different keys in the same Redis DB**, so switching paths
silently resets all counters.

### DEBT-10 · Optional-dependency anti-pattern in `TokenService` · OPEN
*Origin: audit I-6*

The constructor takes `ICachedUserRolesService? = null`, `IRevokedTokenCache? = null`,
`ILogger? = null` (`:37-39`). All three *are* registered, so the null branches are unreachable
today — but dropping a registration makes the **revoked-token check silently disappear**
rather than fail loudly.

### DEBT-11 · `ICacheInvalidationContext` is not registered in Identity · OPEN
*Origin: audit I-8*

Basket (`:61`), Catalog (`:42`) and Ordering (`:43`) all register it; Identity does not. It
does not throw because the behavior's constructor parameter is optional — so
runtime-discovered invalidation keys are a **silent no-op in Identity only**.

### BUG-09 · `UpdateProfileCommand.ProfilePictureUrl` is destructive on partial update · OPEN
*Origin: audit A-3*

`UpdateProfileCommandHandler.cs:39` assigns `user.ProfilePictureUrl = request.ProfilePictureUrl`
unconditionally, while the validator only checks
`.When(x => !string.IsNullOrEmpty(...))` (`:25-28`). A client omitting the field from the JSON
body **silently clears an existing avatar**. `FirstName`/`LastName` are `NotEmpty`-guarded, so
this is the only field with the behaviour. It is the scalar flavour of the repo-wide
"optional members must tolerate explicit null" trap.

### BUG-10 · `ChangePassword` and `ResetPassword` disagree on account state · OPEN
*Origin: audit H-10, second defect*

`ResetPasswordCommandHandler.cs:46` checks `if (!user.IsActive || user.IsDeleted)`.
`ChangePasswordCommandHandler` checks only for `null`. The two password-mutation paths apply
different account-state policy.

### DEBT-12 · Application pipeline order contradicts its own documentation · OPEN
*Origin: audit A-5*

Effective runtime order is **Caching → CacheInvalidation → Transaction → Validation → Logging
→ Handler**, because `Program.cs` calls `AddIdentityInfrastructure` before
`AddIdentityApplication`. `TransactionBehavior`'s XML doc says it *"should run BEFORE
ValidationBehavior… so validation errors don't cause unnecessary transaction overhead"* — the
opposite of what running before validation achieves. Every malformed request pays a
BEGIN/ROLLBACK round-trip.

### DEBT-13 · `[SensitiveData]` applied inconsistently · OPEN
*Origin: audit A-4*

Marked: `LoginCommand.Password`, `RegisterCommand.Password`, `RefreshTokenCommand.RefreshToken`,
`RevokeTokenCommand.RefreshToken`, `ResetPasswordCommand.{Token,NewPassword}`.
**Unmarked:** `ChangePasswordCommand.{CurrentPassword,NewPassword}`, `ConfirmEmailCommand.Token`,
`Verify2FACommand.Code`, `Disable2FACommand.Code`, `LoginCommand.TwoFactorCode`, and every
`*Request` record.

Rescued only by `LoggingBehavior`'s name-based `RedactedPropertyNames`, which happens to
contain `"CurrentPassword"`, `"NewPassword"`, `"Token"`, `"Code"`. **Renaming `Code` →
`OtpCode` would start logging OTPs in cleartext at Information level.** The authoritative
mechanism is the attribute; it is the one missing.

### DEBT-14 · `ForgotPassword` doc/behaviour mismatch and timing side-channel · OPEN
*Origin: audit A-8*

The command deliberately omits `ITransactionalCommand` — *"Not transactional — this is a
read-only lookup + token generation flow"* — yet the handler enqueues an outbox event and calls
`SaveChangesAsync` (`:57-65`), i.e. an untransacted write.

Separately, the enumeration-safe early return skips both token generation and the DB write, so
the "user exists" path is measurably slower. `LoginCommandHandler` defends against exactly this
with a dummy hash; `ForgotPassword` does not.

### DEBT-15 · Catalog's two main-image rules disagree · OPEN
`MappingConfig.cs:17-22` maps `Product → ProductDto.MainImageUrl` by `DisplayOrder` alone,
**ignoring `IsMain`**. `Product → ProductDetailsDto` (`:29-35`) applies the canonical pick
(`IsMain` → `DisplayOrder` → `CreatedAt`).

Currently unreachable — every list path uses `ProductQueryService`'s hand-written projection,
which does apply the canonical rule — so it is a latent trap, not a live bug: pointing any list
query at Mapster silently returns the wrong main image. Fix the config or delete the mapping
before reusing it.

### DEBT-16 · Catalog's `products:list:*` cache cannot be invalidated · OPEN
`ICacheInvalidatingCommand` supports **exact keys only — no wildcards**, by design and by
documented contract: *"Each key must be an exact match — pattern/wildcard-based invalidation
is not supported by `IDistributedCache`"*
(`src/BuildingBlocks/EShop.BuildingBlocks.Application/Caching/ICacheableQuery.cs:56-62`).
The `products:list:*` family embeds every filter/sort/page
parameter in the key, so it cannot be targeted and stays stale for its full 5-minute TTL after
any write.

### DEBT-17 · `Product.Description` is write-once and uncapped · OPEN
`Product.Description` has a private setter assigned only in `Product.Create` (`:60`).
`UpdateProductCommand` carries only `Price` and `StockQuantity`, and `Product` exposes no
method that sets `Description` — so there is **no way to edit or clear a description once the
product exists**. No length rule guards it either: the column is unbounded `text` and neither
the docs nor the validator state a maximum.

Deliberately not fixed: adding a cap the published contract does not mention would create a
fresh docs-vs-reality gap, and an update path is a contract change.

### DEBT-18 · Five overlapping user DTOs with an inconsistent key name · OPEN
*Origin: audit A-1*

`UserDto`, `UserByEmailResponse`, `UserProfileResponse`, `UserContactResponse`,
`UserInRoleResponse`. `UserProfileResponse` is a strict superset of `UserByEmailResponse`.
Three carry the same four fields, but `UserContactResponse` names the key **`UserId`** while
the other two call it **`Id`** — client-visible across `/users/{id}/contact` vs `/auth/login`
vs `/roles/{role}/users`.

### DEBT-19 · `GetUserByEmailQuery` is entirely dead · OPEN
*Origin: audit A-2 · grep-confirmed*

No endpoint sends it; the only references outside its own three files are one unit test. It
maintains a third parallel user DTO for nothing.

### DEBT-20 · Uncoupled soft-delete fields, and `IsDeleted` is unreachable · OPEN
*Origin: audit D-4/D-7*

`IsActive`, `IsDeleted`, `DeletedAt` are three independent public setters, so
`IsDeleted = true; IsActive = true; DeletedAt = null` is constructible. Only
`UserRepository.DeleteAsync` sets them coherently — and grep confirms **that method has no
caller**, so `IsDeleted` can never become `true` at runtime despite six handlers branching on it.

There is also **no EF global query filter** for soft delete: `IsDeleted` is re-checked by hand
in six handlers and **omitted** in `ChangePassword`, `UpdateProfile`, `Enable2FA`, `Verify2FA`,
`Disable2FA`. With `RequireUniqueEmail = true` and no filter, a soft-deleted user's email could
never be re-registered.

### OPS-03 · `docker compose --profile sandbox up` aborts the whole bring-up · OPEN
`docker-compose.yml:685` pins `stripe/stripe-cli:v1`, which no longer exists on Docker Hub —
`failed to resolve reference … not found` aborts every service in the profile, not just the
listener. Name the services you need instead, e.g.
`docker compose --profile sandbox up -d postgres redis rabbitmq catalog-postgres identity-api catalog-api api-gateway`.

Two adjacent traps: all app services sit behind `profiles: [production, sandbox]`, so a bare
`docker compose up` starts only infra; and `up` **does not rebuild**, so a months-stale image
is served silently against a freshly migrated DB. Add `build` / `--force-recreate` after any
source change.

### OPS-04 · Postgres containers are unreachable from the host · OPEN
Every service's Postgres container sits on `data-network` (`internal: true`). Docker silently
drops host port publishing there, so `docker ps` shows the container healthy with an empty
`PORTS` column while host-side `dotnet ef database update` / `psql` get connection-refused.

Workaround documented in
[databases.md](../06-infrastructure/databases.md#local-migrations-against-data-network-postgres-containers).
For a read-only schema check or a throwaway migration run, skip all of it: start your own
`docker run -d -e POSTGRES_PASSWORD=pw -e POSTGRES_DB=<db> -p 55432:5432 postgres:16-alpine`.

### DOC-01 · `scripts/verify-all.sh` Test 6 cannot pass · OPEN
`scripts/verify-all.sh:181-205` accepts only HTTP 400 or 429. Neither is what the endpoint
produces:

- The **throttle** path returns **401**. `LoginCommandHandler.cs:91` fails with
  `new Error("Auth.TooManyAttempts", …)`, and `AuthController.Login` maps every non-
  `Validation.Failed` error to `Status401Unauthorized`. So the status check fails even though
  the body would have matched the script's `"too many"` grep.
- The **rate-limiter** path returns 429 with an **empty body** — `Program.cs:378` sets
  `RejectionStatusCode` but no `OnRejected` writer is configured, so the script's
  body-content check fails instead.

Pre-existing and independent of any recent work: the test cannot pass by either route.

### DOC-02 · `docs/09-appendix/catalog-images-audit.md` carries stale counts · OPEN
§7 (`:431-432`) says the `#region Images` block holds 11 tests and `#region Attributes` 6; both
were already 17 and 7 when last counted and have grown since. §8 lists gap #2 under "Still open
(5)" while its own table row reads "Mostly closed". Its §4.2 "3.2M unnecessary index scans" is
unverified — §9 says so itself. It is a point-in-time record, not a live index.

---

## Low

### Dead code and tombstones (Identity)
- `Infrastructure/Security/ILoginAttemptTracker.cs` — **entire content is a comment**:
  `// This file has been moved to Domain layer for proper layering`. Verified.
- `Infrastructure/Security/LoginAttemptModels.cs` — same tombstone. Verified.
- `Infrastructure/Security/IdentifierHasher.cs` — a pure forwarding shim *"kept for backward
  compatibility"*, creating **two public `IdentifierHasher` types in adjacent namespaces** — an
  easy wrong-import. Handlers already import the Domain one directly.
- Unreferenced members: `UserRepository.{CreateAsync, UpdateAsync, DeleteAsync, AddToRoleAsync,
  GetByOAuthProviderAsync}`; `UserRepository.GetRolesForUsersAsync` (called only from a perf
  test); `TokenService.ValidateTokenAsync` (which also has `catch { return false; }`, swallowing
  the distinction between expired, tampered and malformed);
  `RevokedTokenCache.RemoveFromRevokedCacheAsync`.
- `IdentityMetrics.UpdateActiveAccountLocks` / `UpdateActiveIpBlocks` — gauges declared, never
  set. **They report 0 forever on dashboards.**
- Commented-out Redis `ClientName` config in `Program.cs`.
- `grep -rn "TODO|FIXME|HACK|NotImplementedException"` across `src/` returns **zero matches** —
  the dead code is all unmarked.

### Domain-layer debt (Identity)
- **No DDD building blocks at all** (D-1). Nothing inherits `Entity<T>`, `AggregateRoot<T>` or
  `ValueObject`; exactly two guard clauses in the whole project; zero `DomainException` throw
  sites. Consequently `BaseIdentityDbContext`'s domain-event dispatch is inert, and every
  invariant lives in FluentValidation only — so any non-HTTP write path bypasses all of them.
- **Three domain events declared and never raised** (D-2) — `UserRegisteredEvent`,
  `UserEmailConfirmedEvent`, `UserPasswordResetRequestedEvent`. They shadow three *live*
  integration events of the same names, and can never be raised because
  `ApplicationUser : IdentityUser`, not `AggregateRoot`.
- **Two dead value objects** (D-3) — `ValueObjects/RefreshToken.cs`, `EmailConfirmationToken.cs`.
  Zero references repo-wide.
- **`TwoFactorSecret` is declared, mapped, migrated, and never read or written** (D-5). 2FA uses
  ASP.NET Identity's authenticator key in `user_tokens`. A permanently-null column that looks
  like it holds a secret.
- **`GoogleId`/`GitHubId` are only ever read, never assigned** (D-6), so
  `GetByOAuthProviderAsync` can only return null. Both have filtered unique indexes maintained
  for nothing.
- `LastLoginIp` / `CreatedByIp` / `RevokedByIp` are `varchar(50)` (D-8); an IPv6 address with a
  zone index can exceed that → `22001`. Unexercised because InMemory ignores `HasMaxLength`.
- `RefreshTokenEntity` allows `ExpiresAt < CreatedAt`, and "revoked with no reason" / "reason set
  but not revoked" are both representable (D-9).

### Application/infrastructure nits (Identity)
- `CacheInvalidationBehavior` invalidates on failure too (A-6) — over-invalidation only.
- Key-prefix asymmetry (A-7): `CachingBehavior` honours `UseVersioning`,
  `CacheInvalidationBehavior` unconditionally includes the version. They line up today only
  because Identity sets `UseVersioning = true`; flipping the flag silently breaks every
  invalidation.
- `LoginCommandHandler` redundant round-trips (A-9): `IsLockedOutAsync` computed then re-awaited;
  `GetRolesAsync` re-fetches roles already resolved through the cache, hitting the DB on every
  login.
- Hardcoded PBKDF2 dummy hash in a handler (A-10) — if the hasher's format or iteration count
  changes, the timing equalization silently degrades.
- Hardcoded 2FA issuer `"EShop"` (A-11), not from config.
- `IdentityTelemetry` is a static class with mutable state (A-12) set once from `Program.cs`. In
  any unit test `_metrics` is null and every `Record*` silently no-ops. Also `IIdentityMetrics`
  is declared in Application but implemented in the **API** layer, not Infrastructure. Only 3 of
  15 handlers create an `Activity` at all.
- `LoginCommandHandler` is ~215 lines mixing brute-force policy, timing defence, account-state
  policy, 2FA and token issuance (A-13). The natural home for most of it does not exist because
  of D-1.
- `IpAddress` is client-bindable on `LoginCommand`/`RefreshTokenCommand` (A-14) — overwritten
  server-side, so safe, but it appears in Swagger as client-settable.
- `IRevokedTokenCache` is `AddSingleton` while the equivalent `CachedUserRolesService` is
  `Scoped` (I-9) — one of the two is stylistically wrong. No actual captive-dependency bug exists.
- `ProductCreatedConsumer` is scaffolding (I-10): its handler only logs, writing a
  `processed_messages` row per Catalog product event purely to emit a log line.
- Outbox options are registered as singleton POCOs bypassing `IOptions` (I-11) — batch size,
  polling interval, retries, retention are hardcoded in C#. Matches Catalog and Ordering exactly,
  so it is a repo-wide convention rather than a divergence.
- `IdentityDbContext` declares three constructors (I-13); DI picks the greediest, so the two
  lesser ones are dead weight and a silent-degradation trap.

### API-layer nits (Identity)
- `InternalServiceAuthorization` leaks key length by timing (API-6):
  `CryptographicOperations.FixedTimeEquals` returns false immediately on length mismatch. A blank
  configured key also returns without calling `context.Fail()` — correct outcome, indistinguishable
  from misconfiguration.
- Undeclared status codes (API-7): `AccountController` returns 401 from `GetCurrentUserId()` null
  checks without declaring it; `RolesController` can return 400 from three actions without
  declaring it.
- `UsersController` has no class-level `[Authorize]` (API-8) — the single action carries the
  correct policy, but class-level default-deny is safer. `AuthController` is anonymous by
  *absence* of an attribute rather than explicit `[AllowAnonymous]`; with no fallback
  authorization policy, adding `RequireAuthenticatedUser()` later would silently lock out login.
- Root endpoint advertises wrong routes (API-9): it reports `POST /api/v1/auth/refresh` and
  `/logout`; the real routes are `refresh-token` and `revoke-token`.
- A local function is spliced into the middleware pipeline (API-10) — `IsPostgresStartupException`
  sits between `UseForwardedHeaders()` and `UseMiddleware<UniformResponseTimingMiddleware>()`.
  Compiles fine, but it interrupts the readable top-to-bottom ordering.
- JWT validation gaps (API-12): all four core validations are on with `ClockSkew = TimeSpan.Zero`
  (good); missing `ValidAlgorithms`/`RequireSignedTokens` pinning, `RequireHttpsMetadata`,
  `NameClaimType`/`RoleClaimType`. `MapInboundClaims` left at default is **load-bearing and
  undocumented**: `GetCurrentUserId()` reads `ClaimTypes.NameIdentifier` but `TokenService`
  writes `JwtRegisteredClaimNames.Sub`; it works only because the default inbound mapper rewrites
  `sub` → `nameidentifier`. There is a `?? User.FindFirstValue("sub")` fallback, so it is
  defended — by accident rather than design.
- Migration retry loop has no cancellation token (API-13) — delays shutdown by up to 40s during a
  failing boot. Otherwise correct.
- `ClaimTypes.Role` emits the full Microsoft schema URI in every JWT (I-7), bloating tokens and
  coupling all downstream services to that schema.

### Cross-cutting nits
- **RabbitMQ TLS warning is a no-op in Release.**
  `BuildingBlocks/…/MassTransitServiceCollectionExtensions.cs:63` writes the non-dev "UseSsl
  disabled" security warning to `System.Diagnostics.Debug.WriteLine`, so it can never fire in
  production. **Affects all services.**
- **`Identity/Program.cs` is not valid UTF-8.** Two comment lines contain a bare `0x97`
  (Windows-1252 em-dash). A read-modify-write through a UTF-8-decoding editor turns each into
  U+FFFD, producing spurious diff hunks in comments nobody touched. It is the **only** such file
  in the tracked repo. After editing, check `git diff` for changes outside your edit and restore
  with `perl -0777 -pi -e 's/\xEF\xBF\xBD/\x97/g'`.
- **Gateway traffic simulation is on by default.** `appsettings.json:69-81` —
  `catalog-products-read-route` injects 500/503 at `ErrorRate: 0.02` plus a 20-100 ms delay, and
  `orders-route` likewise. So ~1 in 50 catalog GETs through the gateway fails for no real reason.
  Send `X-Simulate: false` to suppress it per request, or results are contaminated.
- **Catalog rate-limits itself in scripted testing.** A global 100/min per client IP plus the
  `"search"` policy (SEC-03) means a local verification script hits 429s after ~100 calls, and
  the rejections are indistinguishable from real errors unless you check the status code. Pace
  bursty scripts or treat 429 as a harness artifact.

---

## Refuted — do not re-report

Each of these was reported as a defect and is **not one**. They are kept here because a future
pass will otherwise re-derive the same false alarm from the same code.

### REF-01 · The `xmin` migration is not broken
`20260206010404_AddRefreshTokenConcurrencyToken` reads
`AddColumn<uint>(name: "xmin", table: "refresh_tokens", type: "xid")`. Since `xmin` is a
PostgreSQL *system* column that looks like it must fail with `42701`. It does not: Npgsql's
migration SQL generator special-cases the system rowversion column and emits **no DDL at all** —
the generated script is just the `__EFMigrationsHistory` INSERT. Verified against PostgreSQL
16.13; all six migrations apply cleanly. This is the intended
`Property(t => t.Version).IsRowVersion()` mapping, not a scaffolding artifact. Editing it would
only desync the snapshot.

### REF-02 · Identity's Kubernetes Deployment does have probes
Reported as declaring none. It declares both `readinessProbe` and `livenessProbe`
(`k8s/services/microservices.yaml:80-92`) and is in fact the *best*-instrumented service. See
OPS-02 for the real, inverted gap.

### REF-03 · The Dockerfile's missing `Messaging.csproj` does not break the build
Catalog's and Identity's `API/Dockerfile` omit `EShop.BuildingBlocks.Messaging.csproj` from the
restore layer while both services' `Application`/`Infrastructure` csproj reference it. Restore
**succeeds anyway**, printing `Skipping project "…" because it was not found.` — a message, not
an error — and the later `COPY . .` + `dotnet build` restores it normally. The only cost is
Docker layer-cache efficiency. Adding the COPY is a cache optimisation, not a bug fix.
*Testing this needs `docker build --no-cache`; a plain `--target build` reuses a cached restore
layer and proves nothing.*

### REF-04 · Kubernetes does not treat exit 0 as a clean shutdown
`restartPolicy: Always` (the Deployment default) restarts a container on **any** exit, including
0 — verified in-cluster, the pod goes to CrashLoopBackOff. BUG-01 is still real; this particular
consequence of it is not.

### REF-05 · `ProductAttribute` is properly mapped
Reported as having no `OnModelCreating` block and therefore convention-mapped to a singular
`ProductAttribute` table with unbounded `text` columns. **Now stale** — `CatalogDbContext.cs:176+`
has a full block with `ToTable("ProductAttributes")`, `ValueGeneratedNever()` and explicit
lengths, and the model snapshot agrees (`:286`).

### REF-06 · The `bin\Debug` look-alike directories are not currently in the tree
A directory literally named `bin\Debug` — where the `\` is U+F05C, a look-alike glyph, not a path
separator — once defeated `.gitignore` and made a branch unclonable on Windows.
`git ls-files | grep $'\xef\x81\x9c'` returns **0** on this branch, and `.gitignore`'s
`**/bin*Debug/` rule blocks it going forward. **The hazard is still live** — some MSBuild
invocation creates such directories, and neither `[Bb]in/` nor `[Dd]ebug/` matches them — so keep
the detection command, but there is nothing to clean up today.

---

## Cannot be verified from this repository

- **The Next.js client.** `src/ClientApp/eshop-web` is referenced as the frontend but holds no
  tracked source — `git ls-files -- src/ClientApp` returns nothing, and the directory contains
  only local `node_modules/` and `.next/`. It is the only consumer that might still parse the old
  `{ error, message }` error shape, and whether it does cannot be checked here. Treat frontend
  contracts as documented in [Data Contracts.md](../01-overview/Data%20Contracts.md), not as
  readable code.
- **Live Stripe webhook behaviour** — requires real Stripe CLI delivery, which the pinned image
  (OPS-03) currently blocks.
- **Anything requiring a real Postgres.** BUG-02, DEBT-06, DEBT-07 and the `varchar(50)` overflow
  (D-8) are all reasoned from the code and the provider's documented semantics; none is exercised
  by a test in this repo (TEST-01).
