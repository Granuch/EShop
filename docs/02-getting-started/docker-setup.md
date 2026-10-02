# Docker Setup Guide

This guide describes how to run the EShop backend with Docker Compose, what each profile starts, and how the stack is
checked in CI.

---

## Prerequisites

- Docker Desktop, or Docker Engine with the Compose plugin
- At least 8 GB RAM assigned to Docker. The full sandbox + monitoring stack uses about 1.5 GB, and image builds need more.
- A `.env` file (see [Environment Setup](#environment-setup))

---

## Compose Files in the Repository Root

- `docker-compose.yml`: the whole stack
- `docker-compose.override.production.yml`: production overrides. Every database password, the Seq admin password and
  the gateway's real SMTP server are required (`${VAR:?}`), Mailpit is not started, and Seq authentication is on.
- `docker-compose.override.public.yml`: publishes the monitoring UIs on public ports
- `.env.example`: the environment template. It lists every variable the compose files use.

---

## Environment Setup

Every service runs in the `Sandbox` environment by default, and Sandbox is guarded like a deployment. A service
**refuses to start** when a secret is missing or still holds a placeholder such as `CHANGE_ME…`. It exits with code 1
and logs the reason. So a plain copy of the template is not enough.

The quickest working `.env` is the one CI uses. It copies `.env.example` and replaces every `CHANGE_ME` value with a
random one:

```bash
bash .github/scripts/ci-env.sh .env      # overwrites an existing .env
```

Or copy the template and replace each `CHANGE_ME` value yourself:

```bash
cp .env.example .env            # PowerShell: Copy-Item .env.example .env
```

Notes:
- Mail goes to Mailpit by default. Leave `GATEWAY_SMTP_USERNAME`/`GATEWAY_SMTP_PASSWORD` and
  `NOTIFICATION_SMTP_USERNAME`/`NOTIFICATION_SMTP_PASSWORD` **empty** for Mailpit. Mailpit has no authentication, and a
  username makes every email fail. Pointing Notification at a real SMTP server means registrations, resets and order
  emails are really sent.
- Values in your shell override `.env`, which is handy for a one-off change. For example, to try Identity's strict
  mode, where an unconfirmed account cannot even sign in (by default it can, and only placing an order needs the
  confirmed address):
  `IDENTITY_REQUIRE_CONFIRMED_EMAIL=true docker compose --profile sandbox up -d identity-api`.

---

## Profiles

A bare `docker compose up` starts only the infrastructure (the PostgreSQL databases, Redis and RabbitMQ).
Everything else is behind a profile:

| Profile | Adds | Use |
|---|---|---|
| `sandbox` | the seven application services, Mailpit | local development (recommended) |
| `monitoring` | Seq, Prometheus, Grafana, Jaeger, OpenTelemetry Collector, the Postgres and Redis exporters, Redis Commander, Redis Insight | logs, metrics and traces |
| `development` | Mailpit, Redis Commander, Redis Insight | infrastructure only, for services run from the IDE |
| `stripe` | the Stripe CLI webhook listener | **only together with `sandbox`**: on its own, compose rejects it because the listener depends on `payment-api` |
| `production` | the application services | with `-f docker-compose.override.production.yml` |

---

## Start Commands

```bash
# Application stack
docker compose --profile sandbox up -d --wait

# With logs, metrics and traces
docker compose --profile sandbox --profile monitoring up -d --wait

# With the Stripe webhook bridge
docker compose --profile sandbox --profile stripe up -d

# After a source change: rebuild, and refresh base images too
docker compose --profile sandbox build --pull
docker compose --profile sandbox up -d --wait

# Stop (keeps data) / stop and delete all data
docker compose --profile sandbox --profile monitoring down
docker compose --profile sandbox --profile monitoring down -v
```

Things that are easy to get wrong:
- **`up` does not rebuild.** After a source change, run `build` (or `up --build`), or the old image keeps serving.
- **`up` does not recreate a container when only a mounted config file changed**, for example
  `infrastructure/otel-collector/otel-collector-config.yml` or the Prometheus and Grafana provisioning. Run
  `docker compose restart <service>` instead.
- `--wait` returns once every container with a healthcheck is healthy, and fails if one is not. While a rebuild runs,
  the old containers keep answering, so a manual readiness loop can pass too early.

---

## What Runs Where

Default host ports from `.env.example`. Everything except the gateway listens on `127.0.0.1` only.

| Service | URL |
|---|---|
| API Gateway | `http://localhost:7000` |
| Identity API (Scalar UI) | `http://localhost:7001/scalar/v1` |
| Catalog API (Scalar UI) | `http://localhost:7004/scalar/v1` |
| Ordering API (Scalar UI) | `http://localhost:7005/scalar/v1` |
| Basket API (Scalar UI) | `http://localhost:7006/scalar/v1` |
| Payment API (OpenAPI document; no Scalar UI) | `http://localhost:7008/openapi/v1.json` |
| Notification API | no host port; reach it through the gateway or from a container on `eshop-network` |
| Mailpit UI | `http://localhost:8025` |
| RabbitMQ management | `http://localhost:15672` |
| Seq | `http://localhost:5341` |
| Prometheus | `http://localhost:9090` |
| Grafana | `http://localhost:3001` (not 3000, which is the `ui/` dev server's port) |
| Jaeger | `http://localhost:16686` |
| Redis Commander / Redis Insight | `http://localhost:8081` / `http://localhost:8001` |

The PostgreSQL containers declare host ports, but they sit on the internal `data-network`, so Docker does not publish
them. See [databases.md](../06-infrastructure/databases.md#local-migrations-against-data-network-postgres-containers)
for running migrations and `psql` against them.

Scalar and OpenAPI are served in every environment except Production. The gateway serves its own
`/openapi/v1.json` and does not proxy any service's Scalar UI.

### Health endpoints

Every service and the gateway serve `/health` (every check), `/health/ready` and `/health/live`. The gateway's
readiness covers only the gateway itself: a stopped service shows on `/health` and on that service's routes (502/503),
but does not make the gateway unready. See [api-gateway.md](../05-services/api-gateway.md#health-and-metrics).

---

## Seeded Test Data

In Development and Sandbox, each service seeds baseline data on startup (after migrations), so the stack is usable
right away.

**Identity**: a default admin account (see `identity-service.md` → Seed Data):
- Email: `IDENTITY_SEED_ADMIN_EMAIL` (default `admin@eshop.com`)
- Password: `IDENTITY_SEED_ADMIN_PASSWORD` (default `Admin123!`)

**Catalog**: a handful of categories and products for manual testing (see `catalog-service.md` → Seed Data),
covering multiple images, attributes, discounts and draft-status edge cases.

Seeding is idempotent (safe to restart the stack) and never runs in Production.

---

## Images

- All seven service images are built from one Dockerfile template (see any `src/**/Dockerfile`):
  - one `dotnet publish` on `mcr.microsoft.com/dotnet/sdk:10.0.401`;
  - a runtime stage on `aspnet:10.0.12`, which is **Ubuntu 24.04**;
  - run as the non-root UID/GID `999`;
  - a healthcheck on `/health/ready`.
- Every image in `docker-compose.yml` uses an exact version tag, and Dependabot proposes updates.
- The build context is an allowlist (`.dockerignore`): only `src/` is sent, without `bin/`, `obj/`, `*.md` or local
  `*.Local.json` settings files.

---

## How the Stack Is Checked in CI

- **CI** (every push to `master`, `feature/**` and `bug/**`, and pull requests into `master`):
  - lints the Dockerfiles and workflows;
  - renders every profile combination from `.env.example`, including production, and fails on any warning;
  - builds all seven images, with a report-only vulnerability scan.
- **Docker Smoke** (push to `master`, nightly, or `gh workflow run "Docker Smoke" --ref <branch>`):
  1. brings the sandbox + monitoring stack up from a cold start;
  2. walks one customer from sign-up to a delivered order through the gateway;
  3. checks that every service's traces reach Jaeger;
  4. fails on any unexpected error in a service log, or on any container restart. Its logs are kept as a workflow
     artifact.

To run the smoke checks against **your own** running stack, use:

```bash
python .github/scripts/smoke_e2e.py
bash .github/scripts/collect-logs.sh smoke-logs
python .github/scripts/scan_logs.py smoke-logs .github/scripts/smoke-log-allowlist.txt
```

The e2e script refuses to start unless Notification sends to Mailpit, so it cannot send real email.

Do **not** run the full smoke workflow locally with a different `.env`. Every volume and container has a fixed name
(`eshop-*`), so a second stack reuses your data volumes and its `down -v` deletes them.

See [CI/CD Workflow](../07-development-workflow/ci-cd-workflow.md) for details.

---

## Troubleshooting

### A service keeps exiting

```bash
docker compose ps -a
docker compose logs <service-name>
```

`Exited (1)` right after start usually means a startup guard rejected the configuration, for example a missing or
placeholder secret. The last lines of the log name the setting.

### Emails do not arrive in Mailpit

Check which server a service actually uses: `docker exec eshop-notification-api printenv Smtp__Host` (and
`Email__Host` for `eshop-api-gateway`). Mailpit keeps messages in memory, so restarting it empties the inbox.

### Port conflicts

Change the conflicting `*_PORT` value in `.env` and run `up -d` again.

### Clean reset

```bash
docker compose --profile sandbox --profile monitoring down -v
docker compose --profile sandbox --profile monitoring up -d --wait
```

On the first start after a reset, each EF service logs one `[ERR] … FROM "__EFMigrationsHistory"`. That is expected:
EF looks for its history table before creating it.

---

## Related Documents

- [Prerequisites](prerequisites.md)
- [Local Setup](local-setup.md)
- [Infrastructure](../06-infrastructure/)
- [CI/CD Workflow](../07-development-workflow/ci-cd-workflow.md)

---

**Version**: 3.0
**Last Updated**: 2026-09-28
