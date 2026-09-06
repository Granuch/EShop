# Databases

PostgreSQL is the primary relational datastore across backend bounded contexts.

---

## Overview

The platform follows a service-owned data model.

Current PostgreSQL contexts in runtime include:
- Identity
- Catalog
- Ordering
- Payment
- Notification

Basket state is managed in Redis.

---

## Runtime Topology

Root `docker-compose.yml` defines dedicated PostgreSQL containers/configuration per context, with:
- environment-driven credentials and db names
- health checks
- mounted config files
- persistent volumes

Port mappings are controlled through `.env` values.

---

## Application Integration

Services integrate through infrastructure-layer DbContext and repository abstractions.

Common runtime characteristics:
- startup migration handling in selected services
- stricter connection-string validation in non-local environments
- telemetry instrumentation for database operations

---

## Schema and Migration Guidance

- Keep schema ownership within each service boundary.
- Evolve migrations per service project.
- Avoid cross-service shared write schemas.
- Validate migration behavior in integration environments before release.

---

## Local Migrations Against `data-network` Postgres Containers

`docker-compose.yml` attaches every per-service Postgres container to `data-network`,
which is declared `internal: true`. Docker silently drops **host** port publishing for
any container whose only network is internal — even though the same compose file
declares `ports:` mappings (e.g. `127.0.0.1:5433->5432` for `catalog-postgres`) and
`appsettings.Development.json` assumes `localhost:<port>` works. Symptoms: `docker ps`
shows the container healthy with an empty `PORTS` column, and any host-side client
(`psql`, `dotnet ef database update`, a local admin tool) gets connection-refused —
recreating or force-recreating the container does not fix it, because the network
attachment is the cause, not container state.

To run `dotnet ef database update` (or any other host-side DB client) against one of
these databases directly, without editing `docker-compose.yml`:

1. Start a container attached to the **default bridge** network first (for internet
   access — NuGet restore, `dotnet tool install`), then add data-network reachability
   with `docker network connect eshop-data-network <container>`. A container built only
   on `eshop-data-network` cannot reach NuGet at all.
2. Address the target database by its **compose service name** (e.g.
   `Host=catalog-postgres;Port=5432`), not `localhost` — service names resolve via
   Docker DNS on the shared network.
3. If the repo is bind-mounted into that container from a Windows host checkout, delete
   `obj`/`bin` under the mounted paths before restoring: a `project.assets.json` built on
   the host bakes in a Windows-only NuGet fallback-package-folder path, which fails
   restore inside a Linux container with `NETSDK1004` /
   `Unable to find fallback package folder 'D:\...'`. Deleting is safe — the bind mount
   means this affects the host copy too, but `dotnet build`/`restore` regenerates both
   `obj` and `bin` correctly back on the host afterward.

This applies to Identity, Ordering, Payment, and Notification's Postgres containers
exactly the same way as Catalog's, since they share the same `data-network` definition.

---

## Reliability Considerations

- Use health/readiness checks for database-dependent services.
- Apply connection pooling and timeout tuning per service needs.
- Monitor slow queries and failure rates through observability stack.

---

## Security Considerations

- Never commit real database secrets.
- Local `.env` convenience values are acceptable for local development.
- Non-local environments must use secure secret management and non-placeholder values.

---

## Related Documents

- [Caching](caching.md)
- [Message Broker](message-broker.md)
- [Observability](observability.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-05
