# Testing Strategy

Testing strategy for the current backend-focused EShop repository.

---

## Goals

- Protect core business workflows from regressions
- Validate behavior at service boundaries
- Keep feedback loops fast for contributors
- Maintain confidence in releases through repeatable automation

---

## Test Layers

### 1) Unit Tests

Primary purpose:
- Validate domain and application logic in isolation

Typical targets:
- Domain rules and state transitions
- Command/query handlers with mocked dependencies
- Validation behaviors and edge-case handling

### 2) Integration Tests

Primary purpose:
- Validate API/infrastructure behavior with realistic dependencies

Typical targets:
- Endpoint contracts and auth behavior
- Persistence interactions (database/cache)
- Messaging integration paths

### 3) End-to-End/Workflow Validation

Primary purpose:
- Verify critical cross-service workflow continuity

In this backend-first repository, E2E focus is API workflow level unless external UI projects are integrated separately.

### 4) Performance and Reliability Validation

Primary purpose:
- Detect latency, throughput, and stability regressions under load

Typical focus:
- Gateway and service latency under expected traffic
- Error-rate behavior under sustained or burst load

---

## Repository Reality

The solution includes service-level `UnitTests` and `IntegrationTests` projects across backend domains and gateway.

Testing strategy should prioritize these existing projects and expand coverage where behavior changes.

---

## Coverage Guidance

Targets are directional, not vanity metrics:
- Unit tests: broad logic coverage
- Integration tests: critical API and infrastructure paths
- Workflow tests: core business flow checks

Quality and relevance of tests are prioritized over raw percentage.

---

## Test Data and Environment

- Keep tests deterministic and isolated.
- Prefer per-test setup/fixtures over hidden global state.
- Use environment-safe configuration for local/integration runs.
- Keep secrets out of test source and committed config.

---

## CI Expectations

At minimum:
- Build must pass
- Relevant tests must pass for changed areas

For higher-risk changes, include additional integration and workflow checks.

---

## Change Policy

When behavior changes:
- update existing tests or add new ones
- cover failure and edge paths
- update docs if workflow/contracts changed

---

## Related Documents

- [Unit Testing](unit-testing.md)
- [Integration Testing](integration-testing.md)
- [Performance Testing](performance-testing.md)
- [E2E Testing](e2e-testing.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
