#!/usr/bin/env pwsh
# ============================================================
# EShop Kubernetes Deploy Script
# Запускать из корня проекта (где docker-compose.yml)
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host "=== EShop K8s Deploy ===" -ForegroundColor Cyan

# 1. Собрать образы через docker compose
# Имена образов берём из docker-compose build output
Write-Host "`n[1/4] Building images..." -ForegroundColor Yellow
docker compose build

# 2. Тегировать образы под имена из манифестов
# docker compose называет образы как <project>-<service>
# по умолчанию project = папка проекта (eshop)
Write-Host "`n[2/4] Tagging images..." -ForegroundColor Yellow

$services = @(
    @{ Compose = "eshop-identity-api";      K8s = "eshop-identity-api:latest" },
    @{ Compose = "eshop-catalog-api";       K8s = "eshop-catalog-api:latest" },
    @{ Compose = "eshop-basket-api";        K8s = "eshop-basket-api:latest" },
    @{ Compose = "eshop-ordering-api";      K8s = "eshop-ordering-api:latest" },
    @{ Compose = "eshop-payment-api";       K8s = "eshop-payment-api:latest" },
    @{ Compose = "eshop-notification-api";  K8s = "eshop-notification-api:latest" },
    @{ Compose = "eshop-api-gateway";       K8s = "eshop-api-gateway:latest" }
)

foreach ($svc in $services) {
    # Попробуем оба варианта имени (с дефисом и без префикса)
    $sourceImage = $svc.Compose
    $exists = docker image inspect $sourceImage 2>$null
    if (-not $exists) {
        # docker compose иногда использует имя без префикса проекта
        $sourceImage = $svc.Compose -replace "^eshop-", ""
        $exists = docker image inspect $sourceImage 2>$null
    }
    if ($exists) {
        docker tag $sourceImage $svc.K8s
        Write-Host "  Tagged: $sourceImage -> $($svc.K8s)" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: Image not found: $($svc.Compose)" -ForegroundColor Red
        Write-Host "  Run: docker images | grep eshop  to see actual names" -ForegroundColor Red
    }
}

# 3. Применить манифесты
Write-Host "`n[3/4] Applying manifests..." -ForegroundColor Yellow

# Namespace и secrets сначала
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/01-secrets.yaml
kubectl apply -f k8s/02-configmap.yaml

# Инфраструктура
Write-Host "  Applying infrastructure..." -ForegroundColor Gray
kubectl apply -f k8s/infra/

# Ждём пока поднимутся БД и брокер (критично для сервисов)
Write-Host "  Waiting for databases to be ready (60s)..." -ForegroundColor Gray
Start-Sleep -Seconds 10
kubectl wait --for=condition=ready pod -l app=rabbitmq -n eshop --timeout=120s 2>$null
kubectl wait --for=condition=ready pod -l app=redis -n eshop --timeout=60s 2>$null

# Микросервисы и шлюз
Write-Host "  Applying services..." -ForegroundColor Gray
kubectl apply -f k8s/services/

# 4. Статус
Write-Host "`n[4/4] Status:" -ForegroundColor Yellow
kubectl get pods -n eshop
Write-Host ""
kubectl get services -n eshop

Write-Host "`n=== Done! ===" -ForegroundColor Cyan
Write-Host "API Gateway: http://localhost:30700" -ForegroundColor Green
Write-Host "Seq logs:    kubectl port-forward svc/seq 5341:5341 -n eshop" -ForegroundColor Green
Write-Host "RabbitMQ UI: kubectl port-forward svc/rabbitmq 15672:15672 -n eshop" -ForegroundColor Green
Write-Host "Mailpit UI:  kubectl port-forward svc/mailpit 8025:8025 -n eshop" -ForegroundColor Green
Write-Host ""
Write-Host "Logs: kubectl logs deployment/<name> -n eshop -f" -ForegroundColor Gray
Write-Host "Shell: kubectl exec -it deployment/<name> -n eshop -- sh" -ForegroundColor Gray
