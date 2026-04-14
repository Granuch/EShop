# Coding Standards

Coding standards for backend services and shared libraries in this repository.

---

## General Principles

- Follow existing architecture boundaries.
- Prefer consistency with current patterns over introducing new style variants.
- Keep changes minimal and focused.
- Do not introduce dependencies unless required.

---

## C# Naming and Style

- `PascalCase`: types, methods, properties, enum members
- `camelCase`: locals, parameters
- `_camelCase`: private fields
- `UPPER_SNAKE_CASE`: constants (when used by existing style)

Use expressive names and avoid unclear abbreviations.

---

## Async and I/O

- Use async APIs for I/O paths.
- Use `Async` suffix for async methods.
- Avoid `async void` except event handlers.

---

## Layering Rules

Respect service layering:
- API layer: transport concerns and endpoint wiring
- Application layer: use-case orchestration
- Domain layer: core business rules
- Infrastructure layer: external systems and persistence

Avoid leaking infrastructure concerns into domain logic.

---

## Validation and Error Handling

- Use existing FluentValidation + pipeline behavior patterns.
- Return predictable error responses.
- Log exceptions with contextual structured properties.
- Avoid swallowing exceptions silently.

---

## Security and Configuration

- Never commit real secrets.
- Keep local convenient config values local-only.
- Use strict placeholder detection and environment-safe configuration in non-local environments.
- Do not log sensitive data.

---

## Data and Performance

- Avoid N+1 query patterns.
- Query only required fields.
- Use cache and indexes where justified by observed behavior.
- Prefer measurable optimization over speculative changes.

---

## Testing Expectations

For functional code changes:
- Add or update tests for new/changed behavior.
- Cover edge cases and failure paths.
- Keep tests deterministic and readable.

---

## Documentation Expectations

Update docs when:
- API contracts change
- config/runtime behavior changes
- workflow or operational behavior changes

Documentation must remain aligned with current repository reality.

---

## Related Documents

- [Code Review Process](code-review-process.md)
- [Git Workflow](git-workflow.md)
- [CI/CD Workflow](ci-cd-workflow.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
