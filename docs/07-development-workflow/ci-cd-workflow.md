# 🔄 CI/CD Workflow

Continuous Integration / Continuous Deployment pipeline для проєкту.

---

## CI/CD Pipeline Overview

```
┌──────────────┐
│ Git Push     │
│ to feature/* │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────┐
│ CI Pipeline (GitHub Actions)         │
├──────────────────────────────────────┤
│ 1. Code Checkout                     │
│ 2. Build (.NET + React)              │
│ 3. Run Tests (Unit + Integration)    │
│ 4. Code Quality (SonarQube)          │
│ 5. Security Scan (Dependabot)        │
└──────────────────────────────────────┘
       │
       ▼ (if PR to develop)
┌──────────────────────────────────────┐
│ Deploy to DEV Environment            │
└──────────────────────────────────────┘
       │
       ▼ (if merged to develop)
┌──────────────────────────────────────┐
│ Deploy to STAGING Environment        │
└──────────────────────────────────────┘
       │
       ▼ (if PR to main)
┌──────────────────────────────────────┐
│ Production Deployment Approval       │
│ (Manual trigger)                     │
└──────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────┐
│ Deploy to PRODUCTION                 │
│ - Blue-Green Deployment              │
│ - Smoke Tests                        │
│ - Rollback if failure                │
└──────────────────────────────────────┘
```

---

## GitHub Actions Workflows

### 1. Build & Test Workflow

**File**: `.github/workflows/build.yml`

```yaml
name: Build and Test

on:
  push:
    branches: [ develop, main ]
  pull_request:
    branches: [ develop, main ]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Run Unit Tests
      run: dotnet test tests/**/*UnitTests.csproj --no-build --verbosity normal --collect:"XPlat Code Coverage"
    
    - name: Run Integration Tests
      run: dotnet test tests/**/*IntegrationTests.csproj --no-build --verbosity normal
    
    - name: Upload Code Coverage
      uses: codecov/codecov-action@v3
      with:
        files: ./coverage.cobertura.xml
        flags: unittests
        name: codecov-umbrella
    
    - name: SonarQube Scan
      uses: sonarsource/sonarcloud-github-action@master
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
```

**Triggers**:
- Push to `develop` or `main`
- Pull Request to `develop` or `main`

**Duration**: ~5-10 minutes

---

### 2. Docker Build Workflow

**File**: `.github/workflows/docker-build.yml`

```yaml
name: Docker Build and Push

on:
  push:
    branches: [ main, develop ]
    tags:
      - 'v*.*.*'

env:
  REGISTRY: ghcr.io
  IMAGE_PREFIX: ${{ github.repository }}

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    
    strategy:
      matrix:
        service: [identity, catalog, basket, ordering, payment, notification, api-gateway]
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
    
    - name: Log in to Container Registry
      uses: docker/login-action@v3
      with:
        registry: ${{ env.REGISTRY }}
        username: ${{ github.actor }}
        password: ${{ secrets.GITHUB_TOKEN }}
    
    - name: Extract metadata
      id: meta
      uses: docker/metadata-action@v5
      with:
        images: ${{ env.REGISTRY }}/${{ env.IMAGE_PREFIX }}/eshop-${{ matrix.service }}
        tags: |
          type=ref,event=branch
          type=semver,pattern={{version}}
          type=sha,prefix={{branch}}-
    
    - name: Build and push
      uses: docker/build-push-action@v5
      with:
        context: .
        file: ./src/Services/${{ matrix.service }}/Dockerfile
        push: true
        tags: ${{ steps.meta.outputs.tags }}
        labels: ${{ steps.meta.outputs.labels }}
        cache-from: type=gha
        cache-to: type=gha,mode=max
```

**Triggers**:
- Push to `main` or `develop`
- Git tags (e.g., `v1.0.0`)

**Output**: Docker images pushed to GitHub Container Registry

---

### 3. Deploy to DEV Workflow

**File**: `.github/workflows/deploy-dev.yml`

```yaml
name: Deploy to DEV

on:
  push:
    branches: [ develop ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: development
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
    
    - name: Azure Login
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS_DEV }}
    
    - name: Set up kubectl
      uses: azure/setup-kubectl@v3
    
    - name: Get AKS credentials
      run: |
        az aks get-credentials \
          --resource-group eshop-dev-rg \
          --name eshop-dev-aks
    
    - name: Deploy to Kubernetes
      run: |
        kubectl apply -f deploy/k8s/dev/
        
        # Update images
        kubectl set image deployment/catalog-api \
          catalog-api=${{ env.REGISTRY }}/${{ env.IMAGE_PREFIX }}/eshop-catalog:develop-${{ github.sha }} \
          -n eshop-dev
    
    - name: Wait for rollout
      run: |
        kubectl rollout status deployment/catalog-api -n eshop-dev --timeout=5m
    
    - name: Run smoke tests
      run: |
        curl -f https://dev-api.eshop.com/health || exit 1
```

**Environment**: Development (auto-deploy on every push to `develop`)

---

### 4. Deploy to PRODUCTION Workflow

**File**: `.github/workflows/deploy-prod.yml`

```yaml
name: Deploy to PRODUCTION

on:
  push:
    tags:
      - 'v*.*.*'

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: production
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
    
    - name: Azure Login
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS_PROD }}
    
    - name: Get AKS credentials
      run: |
        az aks get-credentials \
          --resource-group eshop-prod-rg \
          --name eshop-prod-aks
    
    - name: Blue-Green Deployment
      run: |
        # Deploy green version
        kubectl apply -f deploy/k8s/prod/green/
        
        # Wait for green to be healthy
        kubectl rollout status deployment/catalog-api-green -n eshop-prod
        
        # Run smoke tests on green
        kubectl run smoke-test \
          --image=curlimages/curl \
          --rm -i --restart=Never \
          --command -- curl -f http://catalog-service-green/health
        
        # Switch traffic to green
        kubectl patch service catalog-service -n eshop-prod \
          -p '{"spec":{"selector":{"version":"green"}}}'
        
        # Wait 5 minutes for monitoring
        sleep 300
        
        # If successful, delete blue
        kubectl delete deployment catalog-api-blue -n eshop-prod
    
    - name: Notify Slack
      uses: slackapi/slack-github-action@v1
      with:
        payload: |
          {
            "text": "🚀 Production deployment successful: ${{ github.ref_name }}"
          }
      env:
        SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK }}
```

**Trigger**: Manual approval required (GitHub Environment protection rule)

---

## Environment Strategy

### 1. Development (DEV)

**Purpose**: Latest code from `develop` branch.

**Deployment**: Automatic on every push to `develop`.

**URL**: `https://dev-api.eshop.com`

**Configuration**:
- Database: `eshop-dev` (Azure PostgreSQL)
- Redis: `eshop-dev-redis`
- Replicas: 1 pod per service
- Resources: Low (for cost savings)

---

### 2. Staging (STG)

**Purpose**: Pre-production testing.

**Deployment**: Manual trigger from `develop` branch.

**URL**: `https://stg-api.eshop.com`

**Configuration**:
- Database: `eshop-stg` (similar to production)
- Redis: `eshop-stg-redis`
- Replicas: 2 pods per service
- Resources: Same as production

**Use for**:
- UAT (User Acceptance Testing)
- Performance testing
- Security testing
- Integration testing with external APIs

---

### 3. Production (PROD)

**Purpose**: Live environment.

**Deployment**: Manual approval required, triggered by Git tags (`v1.0.0`).

**URL**: `https://api.eshop.com`

**Configuration**:
- Database: `eshop-prod` (with backups)
- Redis: `eshop-prod-redis` (replicated)
- Replicas: 3+ pods per service
- Resources: High (auto-scaling enabled)

---

## Deployment Strategies

### 1. Blue-Green Deployment

```
┌────────────────────────────────────────┐
│ Load Balancer (Kubernetes Service)    │
│ Selector: version=blue                 │
└─────────────┬──────────────────────────┘
              │
              ▼
    ┌─────────────────┐
    │  Blue (v1.0.0)  │ ← Current production
    │  3 pods         │
    └─────────────────┘

# Deploy green version
    ┌─────────────────┐
    │ Green (v1.1.0)  │ ← New version
    │  3 pods         │
    └─────────────────┘

# Switch traffic (change selector to version=green)
    ┌─────────────────┐
    │  Blue (v1.0.0)  │ ← Keep for quick rollback
    │  3 pods         │
    └─────────────────┘

# After validation, delete blue
```

**Benefits**:
- Zero downtime
- Instant rollback (change selector back to blue)
- Full validation before switching traffic

---

### 2. Rolling Update

```
# Initial state
Pod 1: v1.0.0 ✅
Pod 2: v1.0.0 ✅
Pod 3: v1.0.0 ✅

# Update starts
Pod 1: v1.1.0 🔄 (creating)
Pod 2: v1.0.0 ✅
Pod 3: v1.0.0 ✅

# One pod updated
Pod 1: v1.1.0 ✅
Pod 2: v1.1.0 🔄
Pod 3: v1.0.0 ✅

# All updated
Pod 1: v1.1.0 ✅
Pod 2: v1.1.0 ✅
Pod 3: v1.1.0 ✅
```

**Used for**: DEV and STG environments (simpler than Blue-Green).

---

## Rollback Strategy

### Automated Rollback

```yaml
# If health checks fail after deployment
- name: Rollback on failure
  if: failure()
  run: |
    kubectl rollout undo deployment/catalog-api -n eshop-prod
    kubectl rollout status deployment/catalog-api -n eshop-prod
```

### Manual Rollback

```bash
# Rollback to previous version
kubectl rollout undo deployment/catalog-api -n eshop-prod

# Rollback to specific revision
kubectl rollout undo deployment/catalog-api --to-revision=2 -n eshop-prod

# View rollout history
kubectl rollout history deployment/catalog-api -n eshop-prod
```

---

## Secrets Management

**GitHub Secrets** (configured in repository settings):

| Secret | Environment | Description |
|--------|-------------|-------------|
| `AZURE_CREDENTIALS_DEV` | Development | Azure service principal |
| `AZURE_CREDENTIALS_PROD` | Production | Azure service principal |
| `SONAR_TOKEN` | All | SonarQube authentication |
| `SLACK_WEBHOOK` | Production | Slack notifications |
| `GITHUB_TOKEN` | All | Auto-generated by GitHub |

**Azure Key Vault** (for runtime secrets):
- Database passwords
- API keys (Stripe, SendGrid)
- JWT signing key

---

## Monitoring

### Post-Deployment Checks

```bash
# 1. Check pod status
kubectl get pods -n eshop-prod

# 2. Check deployment status
kubectl rollout status deployment/catalog-api -n eshop-prod

# 3. Check logs
kubectl logs -f deployment/catalog-api -n eshop-prod

# 4. Check health endpoint
curl https://api.eshop.com/health

# 5. Run smoke tests
curl https://api.eshop.com/api/v1/products
```

### Metrics to Monitor

- **Error rate**: Should be < 0.1% after deployment
- **Response time**: p95 should be < 200ms
- **Request rate**: Monitor for sudden drops
- **CPU/Memory**: Check for spikes

---

## Notifications

### Slack Integration

```yaml
- name: Notify Slack on failure
  if: failure()
  uses: slackapi/slack-github-action@v1
  with:
    payload: |
      {
        "text": "❌ Deployment failed: ${{ github.workflow }}",
        "blocks": [
          {
            "type": "section",
            "text": {
              "type": "mrkdwn",
              "text": "*Deployment Failed*\n*Workflow*: ${{ github.workflow }}\n*Branch*: ${{ github.ref }}\n*Commit*: ${{ github.sha }}"
            }
          }
        ]
      }
  env:
    SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK }}
```

---

## Best Practices

1. ✅ **Always run tests before deployment**
2. ✅ **Deploy to DEV → STG → PROD** (never skip environments)
3. ✅ **Use manual approval for production**
4. ✅ **Monitor metrics for 15 minutes after deployment**
5. ✅ **Have rollback plan ready**
6. ✅ **Notify team in Slack**
7. ✅ **Tag releases** (`v1.0.0`) for traceability

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
