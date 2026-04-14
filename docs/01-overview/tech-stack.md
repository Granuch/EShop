# Technology Stack

## Runtime and Core Platform

| Component | Technology | Version / Target | Purpose |
|-----------|------------|------------------|---------|
| Runtime | .NET | 10 | Primary application platform |
| API Framework | ASP.NET Core | 10 | HTTP APIs, middleware pipeline, health endpoints |
| API Gateway | YARP | 2.x | Reverse proxy, route orchestration, gateway policies |

---

## Service Architecture Stack

| Area | Technology | Purpose |
|------|------------|---------|
| Service Organization | Domain-based microservices | Bounded contexts and independent deployment units |
| Shared Foundation | BuildingBlocks projects | Reusable infrastructure and cross-cutting code |
| Messaging Integration | MassTransit | Typed messaging abstraction and consumer plumbing |
| Broker | RabbitMQ 3.13 (management image) | Asynchronous communication between services |

---

## Data and Persistence

| Component | Technology | Version | Purpose |
|-----------|------------|---------|---------|
| Relational Datastores | PostgreSQL | 16 (alpine images) | Service-owned transactional persistence |
| In-Memory/Data Structure Store | Redis | 7 (alpine image) | Basket state and cache use cases |
| ORM / Data Access | Entity Framework Core | 10 | Persistence mapping and migrations |

### Database Layout in Runtime
- identity-postgres
- catalog-postgres
- ordering-postgres
- payment-postgres
- notification-postgres

This keeps ownership boundaries explicit and avoids shared write access across domains.

---

## Security and Access

| Component | Technology | Purpose |
|-----------|------------|---------|
| Authentication | JWT (ASP.NET auth stack) | Token-based identity validation |
| Authorization | Policy + role-based authorization | Route-level access control |
| Gateway Protections | Rate limiting + proxy guards | Abuse control and downstream protection |
| Internal Service Access | API key header convention | Controlled service-to-service sensitive calls |

---

## Observability Stack

| Capability | Technology | Purpose |
|------------|------------|---------|
| Structured Logging | Serilog | Application logging pipeline |
| Log Aggregation | Seq | Centralized log ingestion and query UI |
| Metrics Backend | Prometheus | Metrics scraping and storage |
| Dashboards | Grafana | Operational dashboards |
| Tracing SDK | OpenTelemetry | Distributed trace instrumentation |
| Collector | OTEL Collector | Telemetry pipeline and forwarding |
| Trace UI/Storage | Jaeger | Trace inspection and service flow analysis |

Additional exporters and monitoring utilities are included in Docker runtime profiles (for example PostgreSQL and Redis exporters).

---

## Email and Notification Integration

| Component | Technology | Purpose |
|-----------|------------|---------|
| SMTP Local Tooling | Mailpit | Local email capture and testing |
| Mail Client Integration | Service-level SMTP settings | Notification and gateway email delivery |

---

## Payment Integration

| Component | Technology | Purpose |
|-----------|------------|---------|
| Payment Provider | Stripe (sandbox-oriented configuration) | Payment intent processing and webhook workflows |
| Listener Runtime | Dedicated stripe-listener container | Local webhook relay/validation flow |

---

## Container and Environment Tooling

| Component | Technology | Purpose |
|-----------|------------|---------|
| Container Runtime | Docker | Local and CI runtime packaging |
| Multi-Service Orchestration | Docker Compose | Full local environment spin-up |
| Environment Configuration | `.env` / `.env.example` | Runtime parameterization for local and shared setup |

For local development, password-based values in environment files are intentionally convenient and can be replaced with secure secret stores for production-like environments.

---

## Testing Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| Test Framework | xUnit | Unit and integration test execution |
| Service-Level Test Projects | Per-service `UnitTests` and `IntegrationTests` | Coverage by bounded context |

---

## Tooling Notes

- The repository is backend-focused; frontend source code is not part of current `src` structure.
- Versions and component choices should be treated as code-driven truth from solution projects and runtime configuration.

---

## Related Documents

- [Project Overview](project-overview.md)
- [Architecture Diagram](architecture-diagram.md)
- [Infrastructure Documentation](../05-infrastructure/)
- [Service Documentation](../04-services/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14