# Phase 1: Foundation and Platform Baseline

**Suggested Duration**: 3-5 working days  
**Primary Owners**: Platform + backend maintainers  
**Current Status**: Baseline implemented, iterate as needed

---

## Goal

Stabilize and maintain the shared platform baseline used by all services.

---

## Current Repository Reality

The baseline already exists:
- .NET 10 solution structure (`EShop.slnx`)
- Service-per-domain layout under `src/Services`
- Shared libraries under `src/BuildingBlocks`
- API Gateway under `src/ApiGateways`
- Root Docker Compose runtime
- Initial observability and health-check infrastructure

---

## Scope of This Phase

1. Keep architectural boundaries consistent across services.
2. Keep shared package versions and build behavior stable.
3. Keep local runtime onboarding reproducible.
4. Keep telemetry and health standards consistent.

---

## Work Items

### 1) Solution and repository hygiene
- Verify solution includes required projects.
- Keep naming, folder conventions, and references consistent.
- Review analyzer/build warnings and remove regressions.

### 2) Shared building blocks
- Maintain reusable behaviors (validation, messaging, telemetry helpers).
- Avoid service-specific logic leakage into shared layers.

### 3) Runtime baseline
- Validate root `docker-compose.yml` profiles for sandbox and monitoring.
- Keep `.env.example` aligned with required runtime variables.

### 4) Platform safeguards
- Keep startup-time configuration validation intact.
- Keep health, readiness, and liveness behavior consistent.

---

## Deliverables

- Stable shared baseline for all domain services.
- Updated baseline documentation where needed.
- No unresolved build issues after baseline updates.

---

## Exit Criteria

- `dotnet build` succeeds for solution.
- Core containers start successfully via compose profile.
- Service conventions remain aligned to current architecture.

---

## Next Phase

- [Phase 2: Identity Service](phase-2-identity.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
