# Git Workflow

Branching and pull request workflow for this repository.

---

## Overview

This project uses a PR-driven workflow with short-lived branches.

Current practical baseline:
- `main` is the stable integration branch
- feature/fix/docs/chore branches are created from `main`
- all changes are merged through pull requests
- no direct commits to protected branches

---

## Branch Naming

Use descriptive, scoped branch names:

- `feature/<short-name>`
- `bugfix/<short-name>`
- `hotfix/<short-name>`
- `docs/<short-name>`
- `chore/<short-name>`

Examples:
- `feature/payment-webhook-hardening`
- `bugfix/basket-checkout-validation`
- `docs/update-services-docs`

---

## Daily Flow

```bash
# 1) Sync local main
git checkout main
git pull origin main

# 2) Create working branch
git checkout -b feature/my-change

# 3) Commit small logical steps
git add .
git commit -m "feat(scope): concise message"

# 4) Push branch
git push -u origin feature/my-change

# 5) Open PR to main
```

---

## Commit Convention

Use Conventional Commits:

- `feat(scope): ...`
- `fix(scope): ...`
- `docs(scope): ...`
- `test(scope): ...`
- `refactor(scope): ...`
- `chore(scope): ...`
- `perf(scope): ...`

Examples:

```bash
git commit -m "feat(ordering): add payment-failure state handling"
git commit -m "fix(gateway): correct route authorization policy"
git commit -m "docs(infrastructure): refresh observability guide"
```

---

## Pull Request Requirements

Before opening or merging a PR:

- Build succeeds
- Relevant tests pass
- Scope is focused
- Documentation is updated when behavior/config changes
- No secrets included

Recommended PR content:
- what changed
- why it changed
- how it was tested
- related issue/task reference

---

## Merge Strategy

Preferred merge style: **Squash and merge** for clean history.

Use merge commits only when preserving branch history is necessary.

---

## Hotfix Flow

For urgent production-impacting issues:

1. Branch from `main` (`hotfix/...`)
2. Apply minimal fix
3. Open prioritized PR
4. Merge after required checks/review
5. Backfill docs/tests if needed

---

## Best Practices

- Keep branches short-lived.
- Rebase/sync with `main` regularly.
- Avoid unrelated changes in one PR.
- Prefer small, reviewable diffs.

---

## Related Documents

- [Code Review Process](code-review-process.md)
- [Coding Standards](coding-standards.md)
- [CI/CD Workflow](ci-cd-workflow.md)
- [Deployment Process](deployment-process.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
