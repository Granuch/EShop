# Phase 9: Testing

**Suggested Duration**: 5-7 working days (ongoing)  
**Primary Owners**: All service maintainers  
**Current Status**: Continuous workstream

---

## Goal

Maintain confidence in service correctness through automated unit and integration testing.

---

## Scope

1. Unit tests for domain/application logic.
2. Integration tests for API and infrastructure paths.
3. Regression tests for critical cross-service workflows.
4. Build and test reliability in CI.

---

## Current Baseline

The repository already contains unit and integration test projects per service and gateway.

---

## Work Items

### 1) Coverage of changed behavior
- For every feature/fix, add or update tests near affected service boundaries.
- Prioritize security, messaging, and state-transition paths.

### 2) Integration stability
- Keep integration test setup deterministic.
- Validate database/cache/message-broker dependent scenarios.

### 3) Contract and workflow checks
- Validate key flow: auth -> catalog -> basket -> ordering -> payment -> notification.
- Protect against regressions in gateway route and policy behavior.

### 4) CI alignment
- Ensure test suites execute consistently in build pipelines.

---

## Deliverables

- Updated tests for all changed behavior.
- Service-level regression protection for critical flows.
- Reliable and repeatable CI test execution.

---

## Exit Criteria

- `dotnet test` passes for relevant projects.
- New behavior has corresponding tests.
- No unresolved critical flaky-test issues.

---

## Next Phase

- [Phase 10: Operations and Delivery](phase-10-devops.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
