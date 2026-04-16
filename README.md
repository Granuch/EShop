# EShop — Cloud-Native Microservices Platform

<div align="center">

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-13-239120?style=for-the-badge&logo=csharp&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![Redis](https://img.shields.io/badge/Redis-7-DC382D?style=for-the-badge&logo=redis&logoColor=white)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3.13-FF6600?style=for-the-badge&logo=rabbitmq&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=for-the-badge&logo=docker&logoColor=white)

Production-grade backend platform built on .NET 10 with DDD, CQRS, and event-driven integration.

[Architecture](#architecture) • [Services](#services) • [Getting Started](#getting-started) • [Documentation Map](#documentation-map) • [Tech Stack](#tech-stack)

</div>

---

## Overview

EShop is an event-driven microservices platform designed with clean service boundaries, strong operational visibility, and pragmatic production-focused defaults.

Key design goals:
- reliability over accidental complexity
- explicit configuration and fail-fast startup validation
- service autonomy with clear ownership boundaries
- observable behavior across all critical workflows

---

## Architecture

```text
Clients
  -> API Gateway (YARP: routing, auth policies, rate limiting)
      -> Identity | Catalog | Basket | Ordering | Payment | Notification
          -> PostgreSQL (service-owned databases) + Redis
          -> RabbitMQ (async workflows via MassTransit)
          -> Seq + Prometheus/Grafana + OpenTelemetry/Jaeger
```

Detailed architecture:
- [Architecture Diagram](docs/01-overview/architecture-diagram.md)
- [C4 Diagrams](docs/03-architecture/c4-diagrams.md)
- [Data Flow](docs/03-architecture/data-flow.md)

### Key Architectural Patterns

| Pattern | Implementation |
|---------|----------------|
| Outbox-Oriented Reliability | Integration events persisted and published through infrastructure patterns |
| CQRS | Command and query responsibilities separated through application handlers |
| DDD Service Boundaries | Domain logic isolated per service with clear ownership |
| Event-Driven Integration | RabbitMQ + MassTransit for asynchronous workflow transitions |
| Circuit Breaker / Retry | Applied in gateway, messaging, and dependency-sensitive paths |
| Health-Driven Operations | Liveness/readiness endpoints for orchestration and diagnostics |

---

## Services

### Identity Service
Authentication and authorization capabilities:
- JWT-based access control
- account lifecycle endpoints
- role/policy support
- internal service authorization support

Documentation: [05-services/identity-service.md](docs/05-services/identity-service.md)

### Catalog Service
Product and category management:
- read/write catalog operations
- validation-driven request handling
- cache-aware read paths

Documentation: [05-services/catalog-service.md](docs/05-services/catalog-service.md)

### Basket Service
Redis-backed basket lifecycle:
- add/update/remove basket items
- checkout initiation into async ordering flow
- user-scoped authorization policies

Documentation: [05-services/basket-service.md](docs/05-services/basket-service.md)

### Ordering Service
Order lifecycle orchestration:
- order creation and status transitions
- integration with payment and notification flows
- asynchronous workflow coordination

Documentation: [05-services/ordering-service.md](docs/05-services/ordering-service.md)

### Payment Service
Payment processing service:
- payment lifecycle handling
- Stripe-oriented integration support
- payment outcome event publication

Documentation: [05-services/payment-service.md](docs/05-services/payment-service.md)

### Notification Service
Event-driven notifications:
- notification event consumers
- SMTP-based delivery pipeline
- operational delivery tracking

Documentation: [05-services/notification-service.md](docs/05-services/notification-service.md)

### API Gateway
YARP gateway and ingress policy enforcement:
- route forwarding to backend services
- auth policies and throttling
- simulation and operational middleware behaviors

Documentation:
- [05-services/api-gateway.md](docs/05-services/api-gateway.md)
- [05-services/api-gateway-runtime-guide.md](docs/05-services/api-gateway-runtime-guide.md)

---

## Project Structure

```text
docs/
├── 01-overview/
├── 02-getting-started/
├── 03-architecture/
├── 04-implementation-plan/
├── 05-services/
├── 06-infrastructure/
├── 07-development-workflow/
├── 08-testing/
└── 09-appendix/
```

Implementation code structure is documented in:
- [Project Overview](docs/01-overview/project-overview.md)
- [Architecture Decisions](docs/03-architecture/architecture-decisions.md)

---

## Event Flow

```text
Client checkout request
   -> Basket service validates basket
      -> Basket publishes checkout event
         -> Ordering creates order
            -> Payment processes payment
               -> Ordering updates state
                  -> Notification sends delivery updates
```

Detailed flow: [03-architecture/data-flow.md](docs/03-architecture/data-flow.md)

---

## Tech Stack

| Category | Technology |
|----------|-----------|
| Runtime | .NET 10, ASP.NET Core |
| Messaging | MassTransit, RabbitMQ |
| Persistence | PostgreSQL, EF Core |
| Caching | Redis, IDistributedCache |
| Gateway | YARP |
| Logging | Serilog, Seq |
| Metrics | Prometheus, Grafana |
| Tracing | OpenTelemetry, Jaeger |
| Validation | FluentValidation |
| Mapping | Mapster |
| Containers | Docker, Docker Compose |

Stack details: [01-overview/tech-stack.md](docs/01-overview/tech-stack.md)

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- Docker Desktop (or Docker Engine + Compose)
- Git

Full prerequisites: [02-getting-started/prerequisites.md](docs/02-getting-started/prerequisites.md)

### Local Setup
1. [Local Setup](docs/02-getting-started/local-setup.md)
2. [Docker Setup](docs/02-getting-started/docker-setup.md)

### Typical Local Endpoints
- API Gateway: `http://localhost:7000`
- Seq: `http://localhost:5341`
- RabbitMQ Management: `http://localhost:15672`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`
- Jaeger: `http://localhost:16686`

---

## API Reference

Service API behavior and route responsibilities are documented in:
- [Services documentation](docs/05-services/)
- [Architecture data flow](docs/03-architecture/data-flow.md)

In development mode, OpenAPI/Scalar endpoints are available per service as configured.

---

## Observability

- Logs: Serilog + Seq
- Metrics: Prometheus + Grafana
- Traces: OpenTelemetry + Jaeger
- Health: `/health`, `/health/ready`, `/health/live` (service-dependent)

Details:
- [06-infrastructure/observability.md](docs/06-infrastructure/observability.md)
- [06-infrastructure/observability-setup.md](docs/06-infrastructure/observability-setup.md)

---

## Security Highlights

- JWT-based authentication and policy enforcement
- strict non-local configuration validation for placeholders/secrets
- role/user-scoped authorization patterns
- environment-aware runtime safeguards

Details: [03-architecture/security-architecture.md](docs/03-architecture/security-architecture.md)

---

## Documentation Map

### 01. Overview
- [Project Overview](docs/01-overview/project-overview.md)
- [Architecture Diagram](docs/01-overview/architecture-diagram.md)
- [Technology Stack](docs/01-overview/tech-stack.md)

### 02. Getting Started
- [Prerequisites](docs/02-getting-started/prerequisites.md)
- [Local Setup](docs/02-getting-started/local-setup.md)
- [Docker Setup](docs/02-getting-started/docker-setup.md)
- [Team Agreement](docs/02-getting-started/team-agreement.md)

### 03. Architecture
- [Architecture Decisions](docs/03-architecture/architecture-decisions.md)
- [C4 Diagrams](docs/03-architecture/c4-diagrams.md)
- [Data Flow](docs/03-architecture/data-flow.md)
- [Design Patterns](docs/03-architecture/design-patterns.md)
- [Security Architecture](docs/03-architecture/security-architecture.md)

### 04. Implementation Plan
- [Phase 1: Foundation](docs/04-implementation-plan/phase-1-foundation.md)
- [Phase 2: Identity](docs/04-implementation-plan/phase-2-identity.md)
- [Phase 3: Catalog](docs/04-implementation-plan/phase-3-catalog.md)
- [Phase 4: Basket](docs/04-implementation-plan/phase-4-basket.md)
- [Phase 5: Ordering](docs/04-implementation-plan/phase-5-ordering.md)
- [Phase 6: Payment](docs/04-implementation-plan/phase-6-payment.md)
- [Phase 7: Notifications](docs/04-implementation-plan/phase-7-notifications.md)
- [Phase 8: Client Integration Track](docs/04-implementation-plan/phase-8-frontend.md)
- [Phase 9: Testing](docs/04-implementation-plan/phase-9-testing.md)
- [Phase 10: Operations and Delivery](docs/04-implementation-plan/phase-10-devops.md)
- [Phase 11: Optimization](docs/04-implementation-plan/phase-11-optimization.md)
- [Phase 12: Launch and Post-Launch](docs/04-implementation-plan/phase-12-launch.md)

### 05. Services
- [API Gateway](docs/05-services/api-gateway.md)
- [API Gateway Runtime Guide](docs/05-services/api-gateway-runtime-guide.md)
- [Identity Service](docs/05-services/identity-service.md)
- [Catalog Service](docs/05-services/catalog-service.md)
- [Basket Service](docs/05-services/basket-service.md)
- [Ordering Service](docs/05-services/ordering-service.md)
- [Payment Service](docs/05-services/payment-service.md)
- [Notification Service](docs/05-services/notification-service.md)

### 06. Infrastructure
- [Databases](docs/06-infrastructure/databases.md)
- [Caching](docs/06-infrastructure/caching.md)
- [Message Broker](docs/06-infrastructure/message-broker.md)
- [Observability](docs/06-infrastructure/observability.md)
- [Observability Setup](docs/06-infrastructure/observability-setup.md)
- [Resilience](docs/06-infrastructure/resilience.md)

### 07. Development Workflow
- [Git Workflow](docs/07-development-workflow/git-workflow.md)
- [Coding Standards](docs/07-development-workflow/coding-standards.md)
- [Code Review Process](docs/07-development-workflow/code-review-process.md)
- [CI/CD Workflow](docs/07-development-workflow/ci-cd-workflow.md)
- [Deployment Process](docs/07-development-workflow/deployment-process.md)

### 08. Testing
- [Testing Strategy](docs/08-testing/testing-strategy.md)
- [Unit Testing Guide](docs/08-testing/unit-testing.md)
- [Integration Testing Guide](docs/08-testing/integration-testing.md)
- [Performance Testing Guide](docs/08-testing/performance-testing.md)
- [End-to-End Testing Guide](docs/08-testing/e2e-testing.md)

### 09. Appendix
- [Glossary](docs/09-appendix/glossary.md)
- [Resources](docs/09-appendix/resources.md)
- [Roadmap](docs/09-appendix/roadmap.md)
- [Success Criteria](docs/09-appendix/success-criteria.md)

---

## Contributing

```sh
# 1. Create feature branch
git checkout -b feature/my-change

# 2. Commit changes
git add .
git commit -m "feat(scope): concise description"

# 3. Push and create PR
git push -u origin feature/my-change
```

Contribution workflow details:
- [07-development-workflow/git-workflow.md](docs/07-development-workflow/git-workflow.md)
- [07-development-workflow/code-review-process.md](docs/07-development-workflow/code-review-process.md)
- [07-development-workflow/coding-standards.md](docs/07-development-workflow/coding-standards.md)

---

## License

This project uses the MIT license.

---

**Documentation Version**: 2.4  
**Last Updated**: 2026-04-14
