# Integration Testing Guide

Integration testing guidance for backend APIs, persistence, and messaging boundaries.

---

## Purpose

Integration tests validate that service components work together correctly under realistic runtime conditions.

---

## What to Cover

Recommended integration targets:
- API request/response behavior
- authentication/authorization behavior for protected routes
- database persistence and retrieval behavior
- Redis/cache interactions (where relevant)
- messaging publish/consume behavior for critical workflows

---

## Test Environment Principles

- Use isolated test configuration per run
- Keep setup reproducible and deterministic
- Prefer realistic infrastructure dependencies for high-value scenarios

Examples:
- in-memory alternatives for fast checks
- containerized dependencies for contract-critical paths

---

## Test Design

Each test should define:
1. initial state/setup
2. operation under test
3. expected external behavior and persisted side-effects

Keep assertions focused on externally observable outcomes.

---

## Auth and Policy Validation

Include integration checks for:
- unauthorized access rejection
- forbidden role/policy behavior
- allowed role/user-path behavior

---

## Data and Messaging Validation

For workflows crossing boundaries:
- verify persisted state transitions
- verify expected event emission/consumption outcomes
- verify failure behavior and retries where relevant

---

## Reliability Rules

- no hidden reliance on previously run tests
- stable teardown/cleanup strategy
- clear fixture setup for each suite

---

## When to Add Integration Tests

Add/update integration tests when changing:
- API contracts
- auth/policy behavior
- persistence model or migration-impacting logic
- cross-service message contracts

---

## Execution

Run integration tests locally for affected services and in CI for protected branches.

---

## Related Documents

- [Testing Strategy](testing-strategy.md)
- [Unit Testing](unit-testing.md)
- [Performance Testing](performance-testing.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
