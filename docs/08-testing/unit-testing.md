# Unit Testing Guide

Unit testing guidance for backend services and shared libraries.

---

## Purpose

Unit tests should validate business and application logic in isolation.

Focus on:
- deterministic behavior
- fast execution
- clear intent
- strong edge-case coverage

---

## What to Unit Test

Recommended targets:
- Domain entities/value-like models and invariants
- Application handlers/use-case logic
- Validation behaviors
- Policy/authorization helper logic where isolated

Avoid unit-testing framework internals or trivial DTO-only mappings unless custom logic exists.

---

## Test Structure

Use clear Arrange / Act / Assert structure.

Naming style example:
- `Method_Scenario_ExpectedResult`

Examples:
- `CreateOrder_WhenBasketIsEmpty_ReturnsValidationError`
- `MarkAsPaid_WhenStatusIsPending_TransitionsSuccessfully`

---

## Mocks and Isolation

Mock only external dependencies of the unit under test.

Typical mocked dependencies:
- repositories
- external service clients
- message publishers

Do not mock the behavior you are trying to verify.

---

## Assertion Quality

- Assert behavior, not implementation details
- Verify meaningful outputs/state changes
- Include negative and boundary scenarios

---

## Reliability Rules

- No dependency on real network or database
- No time-sensitive flaky logic without controlled clocks
- No test order dependencies

---

## Updating Tests with Code Changes

For every behavior change:
- add or update unit tests
- include edge/failure path coverage
- remove or refactor obsolete tests

---

## Execution

Run service-specific or solution-wide unit tests as part of local validation and CI.

---

## Related Documents

- [Testing Strategy](testing-strategy.md)
- [Integration Testing](integration-testing.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
