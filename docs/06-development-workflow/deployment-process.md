# 🚀 Deployment Process

Детальний процес deployment у різні environments.

---

## Deployment Checklist

### Pre-Deployment

- [ ] All tests passing (unit + integration)
- [ ] Code review approved
- [ ] No merge conflicts
- [ ] Database migrations reviewed
- [ ] Breaking changes documented
- [ ] Feature flags configured
- [ ] Rollback plan documented
- [ ] Team notified (Slack message)

### Post-Deployment

- [ ] Health checks passing
- [ ] Smoke tests completed
- [ ] Metrics monitored (15 min)
- [ ] Error logs reviewed
- [ ] Performance verified (p95 < 200ms)
- [ ] User-facing features tested manually
- [ ] Deployment documented (CHANGELOG)

---

## Deployment Workflows

### 1. Deploy to DEV

**Trigger**: Automatic on push to `develop`.

**Steps**:

```bash
# 1. Automated via GitHub Actions
# No manual steps required

# 2. Verify deployment
curl https://dev-api.eshop.com/health

# 3. Test manually (optional)
# Browse to https://dev-app.eshop.com
```

**Rollback**: Automatic on health check failure.

---

### 2. Deploy to STAGING

**Trigger**: Manual, from `develop` branch.

**Steps**:

```bash
# 1. Create deployment request
gh workflow run deploy-staging.yml

# 2. Wait for approval (team lead)
# GitHub Actions will wait for manual approval

# 3. Deployment proceeds automatically

# 4. Run UAT tests
npm run test:e2e -- --env=staging

# 5. Performance test
k6 run tests/performance/load-test.js --env staging
```

**Duration**: ~20 minutes (including approval).

**Rollback**:

```bash
# Rollback via GitHub Actions
gh workflow run rollback-staging.yml
```

---

### 3. Deploy to PRODUCTION

**Trigger**: Manual, via Git tag.

**Prerequisites**:
- ✅ Staging tests passed
- ✅ Security scan passed (no vulnerabilities)
- ✅ Performance benchmarks met
- ✅ Team notified (#releases Slack channel)

**Steps**:

```bash
# 1. Create release branch (if not exists)
git checkout develop
git pull origin develop
git checkout -b release/v1.0.0

# 2. Bump version
# Update version in csproj files, package.json

# 3. Create CHANGELOG
echo "## v1.0.0 (2024-01-15)" >> CHANGELOG.md
echo "### Features" >> CHANGELOG.md
echo "- Product search functionality" >> CHANGELOG.md
echo "### Bug Fixes" >> CHANGELOG.md
echo "- Fixed login redirect issue" >> CHANGELOG.md

# 4. Merge to main
git checkout main
git merge release/v1.0.0
git push origin main

# 5. Create Git tag
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0

# 6. GitHub Actions triggers automatically
# Wait for manual approval in GitHub UI

# 7. Monitor deployment
# GitHub Actions sends Slack notification when done
```

**Duration**: ~30 minutes (including approval and monitoring).

**Post-Deployment Monitoring**:

```bash
# 1. Check pod status
kubectl get pods -n eshop-prod

# 2. Watch logs
kubectl logs -f deployment/catalog-api -n eshop-prod --tail=100

# 3. Grafana dashboard
# Open https://grafana.eshop.com
# Monitor:
# - Error rate (should be < 0.1%)
# - Response time (p95 < 200ms)
# - Request rate (should match expected traffic)
# - CPU/Memory usage

# 4. Run smoke tests
curl https://api.eshop.com/api/v1/products | jq '.'
```

**Rollback**:

```bash
# Option 1: Via GitHub Actions (Blue-Green)
# Switch back to blue version (instant)
kubectl patch service catalog-service -n eshop-prod \
  -p '{"spec":{"selector":{"version":"blue"}}}'

# Option 2: Rollback deployment
kubectl rollout undo deployment/catalog-api -n eshop-prod

# Option 3: Revert Git tag
git tag -d v1.0.0
git push origin :refs/tags/v1.0.0
# Re-deploy previous tag
```

---

## Database Migrations

### Migration Strategy

**Tools**: EF Core Migrations

**Workflow**:

```bash
# 1. Create migration (local)
cd src/Services/Catalog/EShop.Catalog.Infrastructure
dotnet ef migrations add AddProductDescription \
  --startup-project ../EShop.Catalog.API \
  --context CatalogDbContext

# 2. Review generated SQL
dotnet ef migrations script --idempotent \
  --startup-project ../EShop.Catalog.API \
  --context CatalogDbContext \
  --output migration.sql

# 3. Commit migration files
git add Migrations/
git commit -m "feat(catalog): add product description migration"

# 4. Apply migration (automated in deployment)
# EF Core auto-applies migrations on app startup
```

### Migration Best Practices

✅ **Do**:
- Always make migrations **backward-compatible**
- Add default values for new NOT NULL columns
- Use `[Column(TypeName = "varchar(200)")]` for deterministic types
- Test migration on copy of production data

❌ **Don't**:
- Drop columns (mark as deprecated first, drop in next release)
- Rename columns without migration steps
- Add NOT NULL without default value
- Rely on automatic migrations in production

### Breaking Migration Example

```csharp
// ❌ Breaking change (drops column immediately)
migrationBuilder.DropColumn(
    name: "OldColumnName",
    table: "Products");

// ✅ Non-breaking approach (3-phase migration)

// Phase 1 (v1.0.0): Add new column
migrationBuilder.AddColumn<string>(
    name: "NewColumnName",
    table: "Products",
    nullable: true);

// Phase 2 (v1.1.0): Migrate data (deploy with both columns)
migrationBuilder.Sql(@"
    UPDATE Products
    SET NewColumnName = OldColumnName
    WHERE NewColumnName IS NULL
");

// Phase 3 (v1.2.0): Drop old column
migrationBuilder.DropColumn(
    name: "OldColumnName",
    table: "Products");
```

---

## Feature Flags

**Library**: LaunchDarkly / Azure App Configuration

**Purpose**: Deploy code without enabling features.

**Example**:

```csharp
// Startup.cs
builder.Services.AddFeatureManagement();

// Controller
public class ProductsController : ControllerBase
{
    private readonly IFeatureManager _featureManager;
    
    [HttpGet("search")]
    public async Task<IActionResult> SearchProducts([FromQuery] string query)
    {
        if (await _featureManager.IsEnabledAsync("AdvancedSearch"))
        {
            // New implementation
            return Ok(await _searchService.AdvancedSearchAsync(query));
        }
        else
        {
            // Old implementation (fallback)
            return Ok(await _searchService.BasicSearchAsync(query));
        }
    }
}
```

**Benefits**:
- Deploy anytime (feature disabled)
- Enable for beta testers only
- Instant rollback (disable flag, no deployment)
- A/B testing

---

## Canary Deployment (Advanced)

**When to use**: High-risk changes, new features.

**Process**:

```yaml
# Canary deployment (5% of traffic)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: catalog-api-canary
spec:
  replicas: 1 # 5% of total replicas
  selector:
    matchLabels:
      app: catalog-api
      version: canary
---
# Stable deployment (95% of traffic)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: catalog-api-stable
spec:
  replicas: 19 # 95% of total replicas
  selector:
    matchLabels:
      app: catalog-api
      version: stable
```

**Steps**:

1. Deploy canary (5% traffic)
2. Monitor for 30 minutes
3. If successful → gradually increase to 100%
4. If errors → rollback canary immediately

---

## Downtime Maintenance Window

**Planned Downtime**: Avoid if possible, use Blue-Green instead.

**If necessary**:

1. **Schedule**: Off-peak hours (3 AM Sunday)
2. **Notify**: Email users 7 days in advance
3. **Status Page**: https://status.eshop.com
4. **Duration**: Max 1 hour

**Steps**:

```bash
# 1. Put app in maintenance mode
kubectl scale deployment catalog-api --replicas=0 -n eshop-prod

# 2. Display maintenance page (via API Gateway)
# Return 503 Service Unavailable with custom HTML

# 3. Perform maintenance (database upgrade, etc.)

# 4. Restore app
kubectl scale deployment catalog-api --replicas=3 -n eshop-prod

# 5. Verify
curl https://api.eshop.com/health
```

---

## Deployment Communication

### Slack Notification Template

```markdown
🚀 **Production Deployment Scheduled**

**Version**: v1.0.0
**Date**: 2024-01-15 14:00 UTC
**Duration**: ~30 minutes
**Impact**: None (zero-downtime deployment)

**What's New**:
- Product search with filters
- Improved checkout flow
- Bug fixes for login redirect

**Deployment Plan**:
1. Deploy green version (14:00)
2. Run smoke tests (14:10)
3. Switch traffic (14:15)
4. Monitor for 15 minutes (14:15-14:30)

**Rollback Plan**: Instant (switch back to blue)

**Deployer**: @john.doe
**On-Call Engineer**: @jane.smith

**Grafana**: https://grafana.eshop.com/d/prod
**Logs**: https://seq.eshop.com
```

### Post-Deployment Report

```markdown
✅ **Production Deployment Successful**

**Version**: v1.0.0
**Deployed At**: 2024-01-15 14:15 UTC
**Duration**: 25 minutes

**Metrics** (1 hour post-deployment):
- Error rate: 0.02% (✅ target: < 0.1%)
- P95 response time: 180ms (✅ target: < 200ms)
- Request rate: 1,200 req/s (✅ expected)
- Zero downtime (✅)

**Issues**: None

**Next Deployment**: v1.0.1 (hotfix planned for 2024-01-17)
```

---

## Rollback Scenarios

### Scenario 1: High Error Rate

**Symptom**: Error rate > 1% after deployment.

**Action**:

```bash
# Immediate rollback
kubectl patch service catalog-service -n eshop-prod \
  -p '{"spec":{"selector":{"version":"blue"}}}'

# Notify team
# Post in #incidents channel

# Investigation
kubectl logs deployment/catalog-api-green -n eshop-prod | grep ERROR
```

---

### Scenario 2: Performance Degradation

**Symptom**: p95 response time > 500ms.

**Action**:

```bash
# Check metrics in Grafana
# If confirmed, rollback

kubectl rollout undo deployment/catalog-api -n eshop-prod

# Scale up if needed (temporary fix)
kubectl scale deployment/catalog-api --replicas=5 -n eshop-prod
```

---

### Scenario 3: Database Migration Failure

**Symptom**: Migration throws error on startup.

**Action**:

```bash
# 1. Rollback deployment (app won't start)
kubectl rollout undo deployment/catalog-api -n eshop-prod

# 2. Manually rollback migration
dotnet ef migrations remove \
  --startup-project ../EShop.Catalog.API \
  --context CatalogDbContext

# Or manually via SQL
# DROP TABLE IF EXISTS __EFMigrationsHistory;
# -- Restore database from backup
```

---

## Deployment Schedule

| Environment | Frequency | Day/Time | Approver |
|-------------|-----------|----------|----------|
| **DEV** | Continuous | Any time | Automatic |
| **STAGING** | 2-3x/week | Tue, Thu 10 AM | Tech Lead |
| **PRODUCTION** | Weekly | Friday 2 PM | CTO |
| **HOTFIX** | As needed | Any time | On-call + CTO |

**Deployment Freeze**:
- Black Friday week (November)
- Christmas/New Year (December 20 - January 5)
- Major holidays

---

## Troubleshooting

### Deployment Stuck

```bash
# Check pod status
kubectl get pods -n eshop-prod | grep catalog

# Describe pod to see events
kubectl describe pod <pod-name> -n eshop-prod

# Common issues:
# - Image pull error (check image name/tag)
# - Insufficient resources (check node capacity)
# - Health check failing (check /health endpoint)
```

### Logs Not Available

```bash
# Check if pod is running
kubectl get pods -n eshop-prod

# If CrashLoopBackOff, check previous logs
kubectl logs <pod-name> -n eshop-prod --previous

# If ErrImagePull, check image name
kubectl describe pod <pod-name> -n eshop-prod | grep Image
```

---

## Automation

All deployments automated via **GitHub Actions**.

**Manual steps** (production only):
1. Create Git tag
2. Approve deployment in GitHub UI
3. Monitor Grafana for 15 minutes

Everything else is automated! 🎉

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
