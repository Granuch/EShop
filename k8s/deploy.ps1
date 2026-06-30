#!/usr/bin/env pwsh
# ============================================================
# EShop Kubernetes Deploy Script
# Builds service images, pushes them to a local registry, then deploys to k8s.
# ============================================================

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$registryHost = "host.docker.internal:5000"
$registryImagePrefix = $registryHost

$services = @(
    @{ Name = "eshop-identity-api";      Dockerfile = "src/Services/Identity/EShop.Identity.API/Dockerfile" },
    @{ Name = "eshop-catalog-api";       Dockerfile = "src/Services/Catalog/EShop.Catalog.API/Dockerfile" },
    @{ Name = "eshop-basket-api";        Dockerfile = "src/Services/Basket/EShop.Basket.API/Dockerfile" },
    @{ Name = "eshop-ordering-api";      Dockerfile = "src/Services/Ordering/EShop.Ordering.API/Dockerfile" },
    @{ Name = "eshop-payment-api";       Dockerfile = "src/Services/Payment/EShop.Payment.API/Dockerfile" },
    @{ Name = "eshop-notification-api";  Dockerfile = "src/Services/Notification/EShop.Notification.API/Dockerfile" },
    @{ Name = "eshop-api-gateway";       Dockerfile = "src/ApiGateways/EShop.ApiGateway/Dockerfile" }
)

function Ensure-LocalRegistry {
    try {
        $response = Invoke-WebRequest -Uri "http://$registryHost/v2/" -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            return
        }
    }
    catch {
        # Registry not responding yet; try to start the container.
    }

    $registryContainer = docker ps -a --filter "name=^/eshop-registry-host$" --format "{{.Names}}"

    if (-not $registryContainer) {
        Write-Host "Starting local registry on port 5000..." -ForegroundColor Yellow
        docker run -d --restart=always --name eshop-registry-host -p 5000:5000 registry:2 | Out-Null
    }
    else {
        $runningRegistry = docker ps --filter "name=^/eshop-registry-host$" --format "{{.Names}}"
        if (-not $runningRegistry) {
            Write-Host "Starting existing local registry container..." -ForegroundColor Yellow
            docker start eshop-registry-host | Out-Null
        }
    }

    Write-Host "Waiting for the registry to be ready..." -ForegroundColor Yellow
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "http://$registryHost/v2/" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw "Local registry at http://$registryHost is not ready."
}

Write-Host "=== EShop Kubernetes Deploy ===" -ForegroundColor Cyan
Write-Host "Registry health check: $registryHost" -ForegroundColor Gray
Write-Host "Registry push endpoint: $registryImagePrefix" -ForegroundColor Gray

Ensure-LocalRegistry

Write-Host "`n[1/4] Building and pushing images..." -ForegroundColor Yellow
foreach ($svc in $services) {
    $image = "$registryImagePrefix/$($svc.Name):latest"
    Write-Host "  Building $image" -ForegroundColor Gray
    docker build -t $image -f $svc.Dockerfile $repoRoot
    Write-Host "  Pushing  $image" -ForegroundColor Gray
    docker push $image
}

Write-Host "`n[2/4] Applying base manifests..." -ForegroundColor Yellow
kubectl apply -f (Join-Path $repoRoot "k8s/00-namespace.yaml")
kubectl apply -f (Join-Path $repoRoot "k8s/01-secrets.yaml")
kubectl apply -f (Join-Path $repoRoot "k8s/02-configmap.yaml")

Write-Host "`n[3/4] Applying infrastructure..." -ForegroundColor Yellow
kubectl apply -f (Join-Path $repoRoot "k8s/infra/")

Write-Host "  Waiting for Redis and RabbitMQ to become ready..." -ForegroundColor Gray
kubectl wait --for=condition=ready pod -l app=rabbitmq -n eshop --timeout=180s
kubectl wait --for=condition=ready pod -l app=redis -n eshop --timeout=180s

Write-Host "`n[4/4] Applying workloads..." -ForegroundColor Yellow
kubectl apply -f (Join-Path $repoRoot "k8s/services/")

Write-Host "  Verifying rollout..." -ForegroundColor Gray
kubectl rollout status deployment/identity-api -n eshop --timeout=180s
kubectl rollout status deployment/catalog-api -n eshop --timeout=180s
kubectl rollout status deployment/basket-api -n eshop --timeout=180s
kubectl rollout status deployment/ordering-api -n eshop --timeout=180s
kubectl rollout status deployment/payment-api -n eshop --timeout=180s
kubectl rollout status deployment/notification-api -n eshop --timeout=180s
kubectl rollout status deployment/api-gateway -n eshop --timeout=180s

Write-Host "`n[Done] Current cluster state:" -ForegroundColor Cyan
kubectl get pods -n eshop
Write-Host ""
kubectl get services -n eshop
Write-Host ""
Write-Host "API Gateway: http://localhost:30700" -ForegroundColor Green
Write-Host "Seq UI:      kubectl port-forward svc/seq 80:80 -n eshop" -ForegroundColor Green
Write-Host "RabbitMQ UI: kubectl port-forward svc/rabbitmq 15672:15672 -n eshop" -ForegroundColor Green
Write-Host "Mailpit UI:  kubectl port-forward svc/mailpit 8025:8025 -n eshop" -ForegroundColor Green
