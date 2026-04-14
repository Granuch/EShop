# Team Agreement

Working rules for contributors in the EShop repository.

---

## Contents

1. Coding Conventions
2. Git Workflow
3. Pull Request Process
4. Code Review Rules
5. Definition of Done
6. Communication

---

## Coding Conventions

### General

- Follow existing architecture and folder boundaries.
- Keep changes focused and minimal.
- Prefer consistency with existing patterns over introducing new styles.
- Do not add dependencies unless clearly required.

### C# / .NET

- Use `PascalCase` for types, methods, properties.
- Use `camelCase` for local variables and parameters.
- Use `_camelCase` for private fields.
- Use `Async` suffix for asynchronous methods.
- Prefer constructor injection for dependencies.
- Keep domain, application, and infrastructure concerns separated.

### API and Contracts

- Preserve backward compatibility unless a breaking change is explicitly approved.
- Validate inputs and return clear error responses.
- Keep route naming and versioning consistent with existing APIs.

### Configuration and Secrets

- Local development may use convenient `.env` values.
- Non-local environments must use secure secret management.
- Never commit real secrets or credentials.

### Tests

- Add tests for all new behavior.
- Keep unit tests near the changed business logic.
- Add integration tests when changes affect API, persistence, or messaging behavior.

---

## Git Workflow

### Branch naming

- `feature/<short-name>`
- `bugfix/<short-name>`
- `hotfix/<short-name>`
- `docs/<short-name>`
- `chore/<short-name>`

### Commits

Use Conventional Commits:

- `feat(scope): ...`
- `fix(scope): ...`
- `docs(scope): ...`
- `test(scope): ...`
- `refactor(scope): ...`
- `chore(scope): ...`

Commit often with small, atomic changes.

---

## Pull Request Process

Before opening a PR:

1. Rebase or merge latest target branch.
2. Build solution successfully.
3. Run relevant tests.
4. Update documentation when behavior/configuration changed.

PR description must include:

- What changed
- Why it changed
- How it was tested
- Related issue/task link

---

## Code Review Rules

### For authors

- Keep PR scope focused.
- Respond to review feedback promptly.
- Mark conversations as resolved only after changes are applied.

### For reviewers

- Review for correctness, safety, maintainability, and architecture fit.
- Provide specific, respectful comments.
- Distinguish between required changes and optional suggestions.

Target review turnaround: within one business day where possible.

---

## Definition of Done

A change is done when:

- Code matches repository architecture and conventions.
- Required tests are added/updated and pass.
- `dotnet build` succeeds.
- Documentation is updated when relevant.
- No secrets were introduced.
- PR is approved and merged.

---

## Communication

- Use repository issues for bugs and feature requests.
- Use PR discussions for implementation details.
- Escalate production-impacting issues immediately in the team channel.

---

## Related Documents

- [Prerequisites](prerequisites.md)
- [Local Setup](local-setup.md)
- [Docker Setup](docker-setup.md)
- [Development Workflow](../07-development-workflow/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
