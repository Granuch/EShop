# Security Architecture

This document describes the current security architecture for the EShop backend platform.

---

## Security Principles

### 1) Defense in Depth

Security controls are applied across multiple layers:

1. **Environment and network layer**
   - Container/network isolation in local runtime
   - Controlled port exposure through compose settings

2. **Gateway layer**
   - JWT authentication and route authorization policies
   - Rate limiting controls
   - Request pipeline guards and middleware checks

3. **Service layer**
   - Service-level JWT validation where required
   - Input validation and business-rule enforcement

4. **Data and secrets layer**
   - Service-owned databases
   - Environment-based secret/config strategy

---

## Authentication and Authorization

### JWT-Based Authentication

Current stack uses JWT validation with issuer/audience/signing key checks.

Typical flow:
1. Client authenticates via identity endpoints.
2. Token is issued.
3. Client calls gateway with bearer token.
4. Gateway applies policy and routes to downstream service.
5. Downstream service processes request under authenticated context.

### Authorization Policies

The gateway enforces route-level authorization policies (for example authenticated and admin-only paths), reducing unauthorized access surface before requests reach downstream services.

---

## Internal Service Security

For sensitive internal interactions, services support API-key style internal authorization configuration via dedicated headers and settings.

This helps restrict internal-only endpoints and service-to-service sensitive operations.

---

## Input Validation and Request Safety

### Validation Pipeline

Validation is handled centrally through application pipeline behavior (FluentValidation + MediatR behavior).

### Data Access Safety

EF Core-based access reduces risk of unsafe query construction when used through normal query APIs.

### API Boundary Controls

Gateway and service middlewares enforce:
- request-size and policy checks
- structured error handling
- correlation and audit-friendly telemetry

---

## Rate Limiting and Abuse Protection

Rate limiting is configured in gateway runtime and helps reduce abuse and brute-force style traffic pressure.

This applies at ingress level before downstream service execution.

---

## Secret and Configuration Strategy

### Local Development

- Convenient local password-based values in `.env` and local config are allowed.
- This supports fast onboarding and local reproducibility.

### Non-Local Environments

- Placeholder values must be replaced.
- Startup validation and configuration checks are used to fail fast on unsafe settings.
- Real secrets must not be committed to source control.

---

## Transport and Runtime Hardening

Current runtime includes environment-aware HTTPS redirection behavior and forwarded-header handling for reverse-proxy scenarios.

Additional hardening expectations for higher environments:
- TLS termination with trusted certificates
- restricted ingress
- secret store integration
- image and dependency vulnerability scanning

---

## Observability and Security Monitoring

Security-relevant diagnostics are supported through:
- Structured logs (Serilog + Seq)
- Request correlation IDs
- Health/readiness probes
- Metrics and traces (Prometheus + OpenTelemetry + Jaeger)

This enables detection and investigation of auth failures, policy rejections, and runtime anomalies.

---

## Threat Focus Areas

Primary threat categories considered:
- Unauthorized API access
- Credential or secret leakage
- Abuse traffic and brute-force patterns
- Misconfiguration in non-development environments
- Cross-service trust boundary misuse

Mitigation is distributed across gateway policy, service validation, configuration checks, and monitoring.

---

## Security Checklist (Operational)

- JWT signing key is strong and non-placeholder.
- Issuer/audience settings are correct.
- Internal API key settings are set for non-local deployments.
- Exposed ports are intentional.
- Health endpoints are monitored.
- Logs/metrics/traces are available for incident analysis.

---

## Related Documents

- [Architecture Decisions](architecture-decisions.md)
- [Data Flow](data-flow.md)
- [Infrastructure Security and Resilience](../06-infrastructure/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
