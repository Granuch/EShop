# Success Criteria

Definition of success for the current backend platform.

---

## Functional Criteria

### Core API workflows

- Authentication flow works (login/token refresh/protected access).
- Catalog read flows work through gateway routes.
- Basket lifecycle flows work for authenticated use cases.
- Checkout-to-order progression works across service boundaries.
- Payment outcomes are reflected in order workflow.
- Notification-triggering events are processed for critical scenarios.

---

## Technical Criteria

### Architecture integrity

- Service boundaries remain clear (API, domain logic, persistence ownership).
- Gateway policies and routing remain consistent with service contracts.
- Messaging contracts remain compatible across producers/consumers.

### Build and quality

- Solution builds successfully.
- Required tests pass for changed behavior.
- No unresolved high-severity regressions.

### Security baseline

- JWT and authorization policy behavior is enforced as expected.
- Non-local environments reject placeholder secrets/config.
- No hardcoded sensitive credentials in source.

### Reliability baseline

- Health/readiness/liveness endpoints function as expected.
- Critical dependencies (database, cache, broker) are observable and diagnosable.

---

## Observability Criteria

- Logs are available and structured for critical services.
- Metrics are available for gateway and key service paths.
- Traces are available for cross-service flow diagnostics.
- Core dashboards support release and incident verification.

---

## Performance Criteria

Directional acceptance targets should be agreed per release, with emphasis on:
- stable p95 latency for critical API paths
- acceptable error rate under expected load
- no severe regressions vs prior baseline

---

## Documentation Criteria

- Documentation reflects current repository reality.
- Navigation/index links are valid.
- Service and infrastructure docs remain synchronized with runtime behavior.
- **Concrete instance**: [`docs/01-overview/frontend/`](../01-overview/frontend/README.md) is a
  code-verified, authoritative contract set for all 141 application endpoints, each entry checked
  against source, the live OpenAPI document and a real request/response through the running
  sandbox stack — not just read off the C# source. It is the standard the rest of `docs/` is held
  to going forward: it is intended that an endpoint or DTO change updates it in the same change.

---

## Team Workflow Criteria

- PR-based workflow is followed.
- Code review and CI checks are consistently applied.
- Production-impacting changes include rollback/verification notes.

---

## Release Readiness Checklist (Condensed)

A release is considered ready when:

- [ ] Build is green
- [ ] Required tests are green
- [ ] Critical workflows are validated
- [ ] Security and config checks are passed
- [ ] Health and telemetry signals are healthy
- [ ] Rollback path is defined

---

## Beyond Baseline (Optional Maturity)

- stricter SLO-based release gates
- broader failure-mode integration testing
- deeper automation of operational verification

---

## Related Documents

- [Roadmap](roadmap.md)
- [Testing Strategy](../08-testing/testing-strategy.md)
- [Deployment Process](../07-development-workflow/deployment-process.md)
- [Frontend API Contracts](../01-overview/frontend/README.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
