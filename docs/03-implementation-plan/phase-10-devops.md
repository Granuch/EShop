# 🚀 Phase 10: DevOps & Deployment

**Duration**: 2 weeks  
**Team Size**: 1-2 DevOps engineers  
**Prerequisites**: All phases 1-9 completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Kubernetes deployment (AKS/EKS/GKE)
- ✅ CI/CD pipelines (GitHub Actions)
- ✅ Infrastructure as Code (Terraform)
- ✅ Monitoring & Alerting
- ✅ Automated backups
- ✅ Secrets management
- ✅ Blue-Green deployment strategy

---

## Tasks Breakdown

### 10.1 Dockerize All Services

**Estimated Time**: 2 days

**Dockerfile Example (Catalog Service):**

```dockerfile
# src/Services/Catalog/Dockerfile

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["Services/Catalog/EShop.Catalog.API/EShop.Catalog.API.csproj", "Services/Catalog/EShop.Catalog.API/"]
COPY ["Services/Catalog/EShop.Catalog.Application/EShop.Catalog.Application.csproj", "Services/Catalog/EShop.Catalog.Application/"]
COPY ["Services/Catalog/EShop.Catalog.Domain/EShop.Catalog.Domain.csproj", "Services/Catalog/EShop.Catalog.Domain/"]
COPY ["Services/Catalog/EShop.Catalog.Infrastructure/EShop.Catalog.Infrastructure.csproj", "Services/Catalog/EShop.Catalog.Infrastructure/"]
COPY ["BuildingBlocks/", "BuildingBlocks/"]

# Restore dependencies
RUN dotnet restore "Services/Catalog/EShop.Catalog.API/EShop.Catalog.API.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/Services/Catalog/EShop.Catalog.API"
RUN dotnet build "EShop.Catalog.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EShop.Catalog.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EShop.Catalog.API.dll"]
```

**Docker Compose for Local Development:**

```yaml
# docker-compose.override.yml

version: '3.9'

services:
  catalog-api:
    build:
      context: .
      dockerfile: src/Services/Catalog/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=catalog;Username=eshop;Password=eshop123
      - Redis__ConnectionString=redis:6379,password=eshop123
    ports:
      - "5101:80"
    depends_on:
      - postgres
      - redis

  identity-api:
    build:
      context: .
      dockerfile: src/Services/Identity/Dockerfile
    ports:
      - "5102:80"

  basket-api:
    build:
      context: .
      dockerfile: src/Services/Basket/Dockerfile
    ports:
      - "5103:80"

  ordering-api:
    build:
      context: .
      dockerfile: src/Services/Ordering/Dockerfile
    ports:
      - "5104:80"

  api-gateway:
    build:
      context: .
      dockerfile: src/ApiGateways/Dockerfile
    ports:
      - "5000:80"

  webapp:
    build:
      context: ./src/WebApps/eshop-web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
```

---

### 10.2 Kubernetes Deployment

**Estimated Time**: 4 days

**Namespace:**

```yaml
# deploy/k8s/namespace.yaml

apiVersion: v1
kind: Namespace
metadata:
  name: eshop
```

**ConfigMap:**

```yaml
# deploy/k8s/configmap.yaml

apiVersion: v1
kind: ConfigMap
metadata:
  name: eshop-config
  namespace: eshop
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  Redis__ConnectionString: "redis-service:6379"
  RabbitMQ__Host: "rabbitmq-service"
```

**Secrets:**

```yaml
# deploy/k8s/secrets.yaml

apiVersion: v1
kind: Secret
metadata:
  name: eshop-secrets
  namespace: eshop
type: Opaque
stringData:
  postgres-password: "secure-password"
  jwt-secret: "very-secure-jwt-secret-key"
  stripe-secret: "sk_live_..."
```

**Deployment (Catalog Service):**

```yaml
# deploy/k8s/catalog-deployment.yaml

apiVersion: apps/v1
kind: Deployment
metadata:
  name: catalog-api
  namespace: eshop
spec:
  replicas: 3
  selector:
    matchLabels:
      app: catalog-api
  template:
    metadata:
      labels:
        app: catalog-api
    spec:
      containers:
      - name: catalog-api
        image: yourdockerhub/eshop-catalog:latest
        ports:
        - containerPort: 80
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: eshop-secrets
              key: catalog-db-connection
        - name: Redis__ConnectionString
          valueFrom:
            configMapKeyRef:
              name: eshop-config
              key: Redis__ConnectionString
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 10
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: catalog-service
  namespace: eshop
spec:
  selector:
    app: catalog-api
  ports:
  - port: 80
    targetPort: 80
  type: ClusterIP
```

**Ingress (API Gateway):**

```yaml
# deploy/k8s/ingress.yaml

apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: eshop-ingress
  namespace: eshop
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
spec:
  ingressClassName: nginx
  tls:
  - hosts:
    - api.eshop.com
    secretName: eshop-tls
  rules:
  - host: api.eshop.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: api-gateway-service
            port:
              number: 80
```

**HorizontalPodAutoscaler:**

```yaml
# deploy/k8s/hpa.yaml

apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: catalog-api-hpa
  namespace: eshop
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: catalog-api
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

---

### 10.3 Infrastructure as Code (Terraform)

**Estimated Time**: 3 days

**Provider Configuration:**

```hcl
# infrastructure/terraform/main.tf

terraform {
  required_version = ">= 1.0"
  
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "eshop-terraform-state"
    storage_account_name = "eshopterraformstate"
    container_name       = "tfstate"
    key                  = "prod.terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
}
```

**AKS Cluster:**

```hcl
# infrastructure/terraform/aks.tf

resource "azurerm_resource_group" "eshop" {
  name     = "eshop-rg"
  location = "East US"
}

resource "azurerm_kubernetes_cluster" "eshop" {
  name                = "eshop-aks"
  location            = azurerm_resource_group.eshop.location
  resource_group_name = azurerm_resource_group.eshop.name
  dns_prefix          = "eshop"

  default_node_pool {
    name       = "default"
    node_count = 3
    vm_size    = "Standard_D2_v2"
    
    auto_scaling_enabled  = true
    min_count            = 2
    max_count            = 10
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin    = "azure"
    load_balancer_sku = "standard"
  }
}

output "kube_config" {
  value     = azurerm_kubernetes_cluster.eshop.kube_config_raw
  sensitive = true
}
```

**PostgreSQL Database:**

```hcl
# infrastructure/terraform/postgres.tf

resource "azurerm_postgresql_flexible_server" "eshop" {
  name                = "eshop-postgres"
  resource_group_name = azurerm_resource_group.eshop.name
  location            = azurerm_resource_group.eshop.location
  
  version             = "16"
  administrator_login = "eshop_admin"
  administrator_password = var.postgres_password
  
  storage_mb = 32768
  sku_name   = "GP_Standard_D2s_v3"
  
  backup_retention_days        = 7
  geo_redundant_backup_enabled = true
}

resource "azurerm_postgresql_flexible_server_database" "catalog" {
  name      = "catalog"
  server_id = azurerm_postgresql_flexible_server.eshop.id
}

resource "azurerm_postgresql_flexible_server_database" "identity" {
  name      = "identity"
  server_id = azurerm_postgresql_flexible_server.eshop.id
}
```

---

### 10.4 CI/CD Pipeline (GitHub Actions)

**Estimated Time**: 2 days

```yaml
# .github/workflows/deploy.yml

name: Deploy to Production

on:
  push:
    branches: [main]

env:
  REGISTRY: ghcr.io
  CLUSTER_NAME: eshop-aks
  RESOURCE_GROUP: eshop-rg

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        service: [identity, catalog, basket, ordering, payment, notification, api-gateway]
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Log in to Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./src/Services/${{ matrix.service }}/Dockerfile
          push: true
          tags: |
            ${{ env.REGISTRY }}/${{ github.repository }}/eshop-${{ matrix.service }}:${{ github.sha }}
            ${{ env.REGISTRY }}/${{ github.repository }}/eshop-${{ matrix.service }}:latest

  deploy:
    runs-on: ubuntu-latest
    needs: build-and-push
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Set up Kubectl
        uses: azure/setup-kubectl@v3
      
      - name: Get AKS credentials
        run: |
          az aks get-credentials \
            --resource-group ${{ env.RESOURCE_GROUP }} \
            --name ${{ env.CLUSTER_NAME }}
      
      - name: Deploy to Kubernetes
        run: |
          kubectl apply -f deploy/k8s/namespace.yaml
          kubectl apply -f deploy/k8s/configmap.yaml
          kubectl apply -f deploy/k8s/secrets.yaml
          kubectl apply -f deploy/k8s/
          
          # Update images
          kubectl set image deployment/catalog-api \
            catalog-api=${{ env.REGISTRY }}/${{ github.repository }}/eshop-catalog:${{ github.sha }} \
            -n eshop
      
      - name: Wait for rollout
        run: |
          kubectl rollout status deployment/catalog-api -n eshop
          kubectl rollout status deployment/identity-api -n eshop
      
      - name: Run smoke tests
        run: |
          kubectl run smoke-test \
            --image=curlimages/curl:latest \
            --restart=Never \
            --rm \
            -i \
            --command -- curl -f http://catalog-service/health
```

---

### 10.5 Monitoring & Alerting

**Estimated Time**: 2 days

**Prometheus Configuration:**

```yaml
# deploy/k8s/prometheus-config.yaml

apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-config
  namespace: monitoring
data:
  prometheus.yml: |
    global:
      scrape_interval: 15s

    scrape_configs:
      - job_name: 'kubernetes-pods'
        kubernetes_sd_configs:
          - role: pod
        relabel_configs:
          - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_scrape]
            action: keep
            regex: true
          - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_path]
            action: replace
            target_label: __metrics_path__
            regex: (.+)
```

**Grafana Dashboards:**

```yaml
# deploy/k8s/grafana-dashboard.yaml

apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboards
  namespace: monitoring
data:
  eshop-overview.json: |
    {
      "dashboard": {
        "title": "E-Shop Overview",
        "panels": [
          {
            "title": "Request Rate",
            "targets": [
              {
                "expr": "rate(http_requests_total[5m])"
              }
            ]
          }
        ]
      }
    }
```

**Alert Rules:**

```yaml
# deploy/k8s/alert-rules.yaml

apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-rules
  namespace: monitoring
data:
  alert-rules.yml: |
    groups:
      - name: eshop
        interval: 30s
        rules:
          - alert: HighErrorRate
            expr: rate(http_requests_total{status=~"5.."}[5m]) > 0.05
            for: 5m
            labels:
              severity: critical
            annotations:
              summary: "High error rate on {{ $labels.service }}"
              description: "Error rate is {{ $value }} per second"

          - alert: HighMemoryUsage
            expr: container_memory_usage_bytes / container_spec_memory_limit_bytes > 0.9
            for: 5m
            labels:
              severity: warning
```

---

### 10.6 Secrets Management (Azure Key Vault)

**Estimated Time**: 1 day

```hcl
# infrastructure/terraform/keyvault.tf

resource "azurerm_key_vault" "eshop" {
  name                = "eshop-keyvault"
  location            = azurerm_resource_group.eshop.location
  resource_group_name = azurerm_resource_group.eshop.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  enable_rbac_authorization = true
}

resource "azurerm_key_vault_secret" "postgres_password" {
  name         = "postgres-password"
  value        = var.postgres_password
  key_vault_id = azurerm_key_vault.eshop.id
}
```

**CSI Driver:**

```yaml
# deploy/k8s/secrets-store.yaml

apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: azure-keyvault
  namespace: eshop
spec:
  provider: azure
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "true"
    tenantId: "<tenant-id>"
    keyvaultName: "eshop-keyvault"
    objects: |
      array:
        - |
          objectName: postgres-password
          objectType: secret
```

---

## Success Criteria

- [x] All services deployed to Kubernetes
- [x] CI/CD pipeline automated
- [x] Infrastructure provisioned via Terraform
- [x] Monitoring and alerting configured
- [x] Secrets managed securely
- [x] Zero-downtime deployments
- [x] Automated backups configured

---

## Next Phase

→ [Phase 11: Performance Optimization](phase-11-optimization.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
