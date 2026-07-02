#!/usr/bin/env pwsh
# Удалить весь EShop из кластера (namespace + все ресурсы внутри)
kubectl delete namespace eshop
Write-Host "EShop namespace deleted. PVCs also removed." -ForegroundColor Yellow
