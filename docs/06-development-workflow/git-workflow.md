# 🌳 Git Workflow & Branching Strategy

Документація про те, як команда працює з Git.

---

## Branching Strategy: GitFlow (Modified)

Використовуємо **спрощений GitFlow** з 3 основними типами гілок:

```
main (production)
  │
  ├── develop (integration branch)
  │     │
  │     ├── feature/user-authentication
  │     │
  │     ├── feature/product-catalog
  │     │
  │     ├── bugfix/fix-login-redirect
  │     │
  │     └── hotfix/critical-payment-bug
  │
  └── release/v1.0.0
```

---

## Branch Types

### 1. `main` Branch

**Purpose**: Production-ready code.

**Rules**:
- ✅ Always deployable
- ✅ Protected (requires pull request + 2 approvals)
- ✅ Automatically deployed to Production
- ❌ No direct commits
- ❌ No force push

**Naming**: `main`

---

### 2. `develop` Branch

**Purpose**: Integration branch for features.

**Rules**:
- ✅ Latest development code
- ✅ Automatically deployed to Dev environment
- ✅ Must pass all tests before merging features
- ❌ No direct commits (merge via PR only)

**Naming**: `develop`

---

### 3. Feature Branches

**Purpose**: New features or enhancements.

**Naming Convention**:
```
feature/<issue-number>-<short-description>

Examples:
feature/123-user-authentication
feature/456-product-search
feature/789-shopping-cart
```

**Workflow**:

```bash
# 1. Create feature branch from develop
git checkout develop
git pull origin develop
git checkout -b feature/123-user-authentication

# 2. Work on feature (commit often)
git add .
git commit -m "feat: add JWT token generation"

# 3. Push to remote
git push origin feature/123-user-authentication

# 4. Create Pull Request to develop
# 5. After approval, merge (squash commits)
# 6. Delete feature branch
```

**Lifespan**: Short-lived (max 1-2 weeks)

---

### 4. Bugfix Branches

**Purpose**: Fix bugs in develop branch.

**Naming Convention**:
```
bugfix/<issue-number>-<short-description>

Examples:
bugfix/234-fix-login-redirect
bugfix/567-product-image-upload
```

**Workflow**: Same as feature branches.

---

### 5. Hotfix Branches

**Purpose**: Critical bugs in production.

**Naming Convention**:
```
hotfix/<issue-number>-<short-description>

Examples:
hotfix/999-payment-processing-error
hotfix/888-security-vulnerability
```

**Workflow**:

```bash
# 1. Create hotfix from main (not develop!)
git checkout main
git pull origin main
git checkout -b hotfix/999-payment-processing-error

# 2. Fix the bug
git add .
git commit -m "fix: resolve payment processing timeout"

# 3. Create PR to main (for immediate production deployment)
git push origin hotfix/999-payment-processing-error

# 4. After merge to main, also merge to develop
git checkout develop
git merge main
git push origin develop

# 5. Delete hotfix branch
```

**Lifespan**: Very short (< 1 day)

---

### 6. Release Branches

**Purpose**: Prepare new production release.

**Naming Convention**:
```
release/v<major>.<minor>.<patch>

Examples:
release/v1.0.0
release/v1.1.0
release/v2.0.0
```

**Workflow**:

```bash
# 1. Create release branch from develop
git checkout develop
git pull origin develop
git checkout -b release/v1.0.0

# 2. Final testing, bug fixes, version bumps
# No new features allowed!

# 3. Merge to main
git checkout main
git merge release/v1.0.0
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin main --tags

# 4. Merge back to develop
git checkout develop
git merge release/v1.0.0
git push origin develop

# 5. Delete release branch
```

**Lifespan**: Few days (during release testing)

---

## Commit Message Convention (Conventional Commits)

**Format**:
```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types**:

| Type | Description | Example |
|------|-------------|---------|
| **feat** | New feature | `feat(catalog): add product search` |
| **fix** | Bug fix | `fix(auth): resolve token expiration` |
| **docs** | Documentation | `docs(readme): update setup instructions` |
| **style** | Code style (formatting) | `style(catalog): format with Prettier` |
| **refactor** | Code refactoring | `refactor(basket): extract validation logic` |
| **test** | Tests | `test(ordering): add unit tests for Order` |
| **chore** | Build, tooling | `chore(deps): update EF Core to 9.0` |
| **perf** | Performance improvement | `perf(catalog): add Redis caching` |

**Examples**:

```bash
# Good commits
git commit -m "feat(auth): implement JWT refresh token"
git commit -m "fix(basket): resolve race condition in checkout"
git commit -m "docs(api): update OpenAPI spec for Orders"
git commit -m "test(catalog): add integration tests for ProductRepository"
git commit -m "perf(catalog): optimize product query with index"

# Bad commits (avoid)
git commit -m "update code"
git commit -m "fix bug"
git commit -m "WIP"
```

**Detailed Commit**:

```
feat(ordering): implement order cancellation

Add ability for users to cancel orders within 10 minutes of placement.
- Add CancelOrderCommand
- Add business rule: only pending orders can be cancelled
- Publish OrderCancelledEvent for notification service
- Add API endpoint: POST /api/v1/orders/{id}/cancel

Closes #456
```

---

## Pull Request Process

### 1. Create Pull Request

**Title**: Same as commit message convention
```
feat(catalog): add product filtering by price range
```

**Description Template**:

```markdown
## Description
Brief description of what this PR does.

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)

## Related Issues
Closes #123

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing completed

## Screenshots (if applicable)
[Add screenshots here]

## Checklist
- [ ] Code follows project style guidelines
- [ ] Self-review completed
- [ ] Tests passing locally
- [ ] Documentation updated
```

---

### 2. Code Review

**Reviewers**: Minimum 2 approvals required.

**Review Checklist**:

- [ ] **Code Quality**
  - Clean, readable code
  - Follows SOLID principles
  - No code duplication
  - Proper error handling

- [ ] **Architecture**
  - Follows Clean Architecture layers
  - Domain logic in Domain layer
  - Infrastructure concerns in Infrastructure layer

- [ ] **Testing**
  - Unit tests for new features
  - Tests cover edge cases
  - No failing tests

- [ ] **Security**
  - No hardcoded secrets
  - Input validation present
  - Proper authorization checks

- [ ] **Performance**
  - No N+1 queries
  - Proper caching if needed
  - Database indexes if needed

- [ ] **Documentation**
  - XML comments for public APIs
  - README updated if needed
  - API documentation updated

**Review Comments**:

```markdown
# Good review comments
❌ Instead of: "This is wrong"
✅ Better: "Consider using FirstOrDefaultAsync() instead of First() to avoid throwing exception when no results found."

❌ Instead of: "Bad code"
✅ Better: "This method has high cyclomatic complexity (15). Consider extracting the validation logic into a separate method."

# Questions
❓ "Why did you choose List<T> over IEnumerable<T> here?"

# Suggestions
💡 "You might want to add a try-catch block here to handle potential network errors."

# Praise
🎉 "Great use of the Repository pattern! This makes testing much easier."
```

---

### 3. Merge Strategy

**For feature branches** → `develop`:
- ✅ **Squash and merge** (clean history)
- Combines all commits into one

**For release branches** → `main`:
- ✅ **Merge commit** (preserve history)
- Creates explicit merge point

**For hotfix branches** → `main`:
- ✅ **Merge commit**

---

## Branch Protection Rules

### `main` Branch

```yaml
Protection Rules:
  - Require pull request before merging: ✅
  - Require approvals: 2
  - Dismiss stale reviews: ✅
  - Require review from Code Owners: ✅
  - Require status checks to pass: ✅
    - CI Build
    - Unit Tests
    - Integration Tests
    - SonarQube
  - Require branches to be up to date: ✅
  - Require conversation resolution: ✅
  - Do not allow bypassing settings: ✅
  - Allow force pushes: ❌
  - Allow deletions: ❌
```

### `develop` Branch

```yaml
Protection Rules:
  - Require pull request before merging: ✅
  - Require approvals: 1
  - Require status checks to pass: ✅
    - CI Build
    - Unit Tests
  - Allow force pushes: ❌
```

---

## Git Workflow Examples

### Example 1: New Feature

```bash
# 1. Start from develop
git checkout develop
git pull origin develop

# 2. Create feature branch
git checkout -b feature/123-product-filtering

# 3. Work on feature
# ... make changes ...
git add src/Services/Catalog/
git commit -m "feat(catalog): add price range filtering"

# ... more changes ...
git add tests/Catalog.Tests/
git commit -m "test(catalog): add tests for price filtering"

# 4. Push to remote
git push origin feature/123-product-filtering

# 5. Create Pull Request on GitHub
# Title: feat(catalog): add price range filtering
# Base: develop ← Compare: feature/123-product-filtering

# 6. Address review comments (if any)
# ... make changes ...
git add .
git commit -m "refactor(catalog): extract validation to separate method"
git push origin feature/123-product-filtering

# 7. After approval, merge via GitHub (Squash and Merge)

# 8. Delete branch locally
git checkout develop
git pull origin develop
git branch -d feature/123-product-filtering
```

---

### Example 2: Hotfix

```bash
# 1. Start from main (production)
git checkout main
git pull origin main

# 2. Create hotfix branch
git checkout -b hotfix/999-payment-timeout

# 3. Fix the bug
git add src/Services/Payment/
git commit -m "fix(payment): increase timeout to 30s"

# 4. Push and create PR to main
git push origin hotfix/999-payment-timeout

# 5. After merge to main, also merge to develop
git checkout develop
git pull origin develop
git merge main
git push origin develop

# 6. Tag release
git checkout main
git tag -a v1.0.1 -m "Hotfix: Payment timeout"
git push origin v1.0.1
```

---

### Example 3: Syncing with Upstream

```bash
# Keep feature branch up-to-date with develop

git checkout feature/123-product-filtering

# Option 1: Rebase (cleaner history)
git fetch origin
git rebase origin/develop

# Option 2: Merge (preserves commits)
git merge origin/develop

# Push (force if rebased)
git push origin feature/123-product-filtering --force-with-lease
```

---

## Git Hooks (Optional)

### Pre-commit Hook

```bash
#!/bin/sh
# .git/hooks/pre-commit

# Run code formatter
dotnet format

# Run tests
dotnet test

# Check for secrets
if git diff --cached | grep -E "(password|secret|api_key)" --color=always; then
  echo "ERROR: Potential secret found in commit!"
  exit 1
fi
```

### Commit-msg Hook

```bash
#!/bin/sh
# .git/hooks/commit-msg

# Validate commit message format
commit_msg=$(cat "$1")

if ! echo "$commit_msg" | grep -qE "^(feat|fix|docs|style|refactor|test|chore|perf)(\(.+\))?: .+"; then
  echo "ERROR: Commit message must follow Conventional Commits format"
  echo "Example: feat(catalog): add product search"
  exit 1
fi
```

---

## Common Git Commands

### Daily Workflow

```bash
# Start work
git checkout develop
git pull origin develop
git checkout -b feature/my-feature

# Commit changes
git add .
git commit -m "feat(scope): description"

# Push to remote
git push origin feature/my-feature

# Update feature branch
git fetch origin
git rebase origin/develop

# Delete local branch
git branch -d feature/my-feature

# Delete remote branch
git push origin --delete feature/my-feature
```

### Fixing Mistakes

```bash
# Undo last commit (keep changes)
git reset --soft HEAD~1

# Undo last commit (discard changes)
git reset --hard HEAD~1

# Amend last commit message
git commit --amend -m "new message"

# Revert a merged PR
git revert -m 1 <merge-commit-hash>

# Stash changes temporarily
git stash
git stash pop
```

---

## Versioning (SemVer)

**Format**: `MAJOR.MINOR.PATCH`

**Examples**:
- `v1.0.0` - Initial release
- `v1.1.0` - New feature added
- `v1.1.1` - Bug fix
- `v2.0.0` - Breaking change

**When to increment**:
- **MAJOR**: Breaking API changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes (backward compatible)

---

## Team Agreement

- ✅ All code changes via Pull Requests
- ✅ Minimum 2 approvals before merging to `main`
- ✅ All tests must pass before merge
- ✅ Delete branches after merge
- ✅ Use conventional commit messages
- ✅ Keep feature branches short-lived (< 2 weeks)
- ✅ Rebase feature branches daily to avoid conflicts

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
