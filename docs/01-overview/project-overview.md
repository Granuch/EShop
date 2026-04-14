# Project Overview

## Project Name

**E-Shop Microservices**

## Project Type

Distributed backend platform for an e-commerce domain.

## Architectural Style

Microservices architecture with domain-based service boundaries.

## Current Maturity

Actively evolving implementation with production-oriented patterns for security, observability, and reliability.

---

## Goals

### Primary Goal
Build and maintain a realistic .NET 10 microservices reference system that supports core commerce workflows and operational excellence.

### Engineering Goals
1. Apply bounded contexts and service ownership across the domain.
2. Use event-driven integration for asynchronous business flows.
3. Keep code organized with layered architecture and shared building blocks.
4. Support secure API exposure through a centralized gateway.
5. Provide repeatable local and containerized environments.
6. Maintain observable services with logs, metrics, and traces.

### Functional Goals
- Identity and account management with token-based authentication.
- Product and category management.
- Basket lifecycle and checkout orchestration.
- Order processing and status transitions.
- Payment execution with Stripe-backed flow and event propagation.
- Notification delivery for key business events.

---

## Core Capabilities

### Domain Services
- **Identity Service**: authentication, authorization, account endpoints.
- **Catalog Service**: product and category APIs.
- **Basket Service**: basket storage and checkout trigger.
- **Ordering Service**: order lifecycle and order queries.
- **Payment Service**: payment processing and result publishing.
- **Notification Service**: outbound notification workflows.
- **API Gateway**: centralized entrypoint and policy enforcement.

### Platform Capabilities
- JWT authentication and role-based authorization.
- API routing and policy enforcement through YARP.
- Redis-backed caching and basket persistence.
- PostgreSQL persistence with service-specific databases.
- RabbitMQ + MassTransit asynchronous communication.
- OpenTelemetry-based distributed telemetry.
- Centralized structured logs with Serilog + Seq.
- Metrics collection with Prometheus and dashboards via Grafana.
- Health, readiness, and liveness probes across services.

---

## Scope Boundaries

The current repository focuses on backend services and runtime infrastructure.

### Not in Current Scope
- UI application source code in this repository.
- Marketplace and multi-tenant features.
- Cross-region deployment topology.
- Advanced shipping integrations.
- Full enterprise compliance scope (for example full PCI program ownership).

---

## Architecture Principles

### 1. Bounded Contexts
Each service owns its domain model, API contracts, and persistence logic.

### 2. Database per Service
Service-level PostgreSQL databases reduce direct coupling.
Basket state is stored in Redis for low-latency access.

### 3. Event-Driven Integration
Cross-service workflows are coordinated with events over RabbitMQ using MassTransit.

### 4. Gateway-First External Access
Clients access backend APIs through the API Gateway for routing, auth policies, and request controls.

### 5. Operational Visibility by Default
Telemetry, health checks, and structured logging are part of service runtime configuration.

---

## Runtime and Delivery Model

### Local Development
- Native .NET service execution is supported.
- Docker Compose is available for full-stack local runtime.
- Environment files provide convenient local password-based configuration and placeholders that can be replaced by secure secret sources in higher environments.

### Quality and Verification
- Unit and integration test projects are organized per service.
- Health endpoints and observability tools support runtime validation.
- CI/CD workflows are designed for automated validation and packaging.

---

## Success Criteria

- All core services run and communicate reliably through the defined contracts.
- Core business flow works end to end: auth -> catalog -> basket -> ordering -> payment -> notification.
- Documentation remains aligned with code and runtime behavior.
- Local onboarding is reproducible for contributors.

---

## Related Documents

- [Architecture Diagram](architecture-diagram.md)
- [Technology Stack](tech-stack.md)
- [Service Documentation](../05-services/)
- [Roadmap](../09-appendix/roadmap.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
