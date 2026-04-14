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

**Version**: 2.0  
**Last Updated**: 2026-04-14
