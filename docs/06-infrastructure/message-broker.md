# Message Broker (RabbitMQ + MassTransit)

Asynchronous service integration is implemented with RabbitMQ transport and MassTransit abstractions.

---

## Overview

Messaging infrastructure enables:
- Decoupled cross-service workflows
- Event-based integration between bounded contexts
- Consumer retry/circuit-breaker support
- Health monitoring for broker connectivity
- Endpoint naming and transport behavior standardization

---

## Runtime Setup

Root `docker-compose.yml` includes RabbitMQ (`3.13-management-alpine`) with:
- AMQP and management UI ports
- environment-driven credentials
- health checks
- persistent data volume

---

## Integration Model in Services

Services register messaging via shared infrastructure extension methods.

Common behavior includes:
- centralized RabbitMQ settings binding
- MassTransit endpoint configuration
- retry and circuit-breaker policies
- endpoint naming conventions
- optional delayed redelivery configuration

Messaging registration is environment-aware and applies stricter validation outside development/testing.

---

## Event Workflow Pattern

Typical pattern:
1. Service performs local transaction.
2. Integration event is published.
3. One or more consumers process event asynchronously.
4. Downstream services emit follow-up events as needed.

This supports eventual consistency while reducing direct runtime coupling.

---

## Reliability Considerations

- Keep consumers idempotent where possible.
- Monitor queue depth and retry behavior.
- Use dead-letter/retry visibility to detect failing workflows.
- Keep message contract changes backward-compatible when possible.

---

## Security Considerations

- Use non-placeholder credentials in non-local environments.
- Prefer TLS-enabled broker configuration in production-like environments.
- Limit management interface exposure.

---

## Operational Diagnostics

Use:
- RabbitMQ management UI
- service logs with correlation context
- traces/metrics to correlate publish-consume latency

---

## Related Documents

- [Resilience](resilience.md)
- [Observability](observability.md)
- [Ordering Service](../05-services/ordering-service.md)
- [Payment Service](../05-services/payment-service.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
