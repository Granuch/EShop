# Deployment Process

Deployment process guidance for backend services and gateway.

---

## Goals

- Safe and repeatable deployments
- Fast validation after release
- Clear rollback path for failures

---

## Pre-Deployment Checklist

- Build succeeds
- Required tests pass
- Config changes reviewed
- Secrets are not hardcoded
- Migration impact assessed (if applicable)
- Rollback approach prepared

---

## Deployment Flow (Generic)

1. Prepare release candidate
2. Deploy to non-production environment
3. Run smoke and integration checks
4. Approve promotion
5. Deploy to production
6. Monitor health and telemetry

---

## Post-Deployment Validation

Immediately verify:
- Health endpoints (`/health`, `/health/ready`, `/health/live` where applicable)
- Error rate and latency in observability tools
- Core business paths through gateway

---

## Rollback Guidance

Rollback should be possible by one of:
- reverting to prior image/artifact version
- restoring previous service revision
- reverting configuration change causing regression

Rollback trigger conditions should be predefined (for example high error rate, failing readiness, severe latency regression).

---

## Database Change Guidance

For schema-affecting changes:
- prefer backward-compatible migration steps
- avoid destructive schema changes in single-step releases
- test migration + rollback paths before production use

---

## Production Safety Notes

- Keep deployment windows and ownership clear.
- Use approval gates for high-risk changes.
- Capture incident notes and follow-up actions after degraded releases.

---

## Related Documents

- [CI/CD Workflow](ci-cd-workflow.md)
- [Resilience](../06-infrastructure/resilience.md)
- [Observability Setup](../06-infrastructure/observability-setup.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
