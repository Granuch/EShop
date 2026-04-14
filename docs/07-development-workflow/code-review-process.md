# Code Review Process

Code review process for quality, consistency, and shared ownership.

---

## Review Goals

- Catch defects early
- Preserve architecture and coding consistency
- Improve maintainability and security
- Share domain and implementation knowledge

---

## Author Responsibilities

Before requesting review:

- Build succeeds
- Relevant tests pass
- Scope is focused and self-reviewed
- Documentation updated if required
- No debug leftovers or commented dead code
- No secrets introduced

---

## Reviewer Responsibilities

Review for:

1. Correctness
2. Architecture fit
3. Security implications
4. Test adequacy
5. Operational impact (logs/metrics/config)

Review comments should be specific and actionable.

---

## What to Check

### Functionality
- Does behavior match intended change?
- Are edge cases and failures handled?

### Architecture
- Are layer boundaries respected?
- Is coupling introduced unnecessarily?

### Security
- Any missing auth checks?
- Any sensitive data exposure?

### Performance
- Any obvious expensive query or allocation patterns?
- Is caching/data access strategy appropriate?

### Tests
- Are key paths covered?
- Are tests clear and deterministic?

---

## PR Quality Guidelines

Good PR characteristics:
- Single responsibility
- Clear title and description
- Linked issue/task when available
- Explicit test evidence

Preferred PR sections:
- Summary
- Motivation
- Testing
- Breaking changes (if any)

---

## Approval and Merge

- Merge only after required approvals and green checks.
- Resolve requested changes before merge.
- Use squash merge by default unless history preservation is required.

---

## Review Etiquette

- Keep feedback respectful and objective.
- Separate required fixes from optional suggestions.
- Ask clarifying questions when intent is unclear.

---

## Related Documents

- [Coding Standards](coding-standards.md)
- [Git Workflow](git-workflow.md)
- [Deployment Process](deployment-process.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
