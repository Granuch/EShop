#!/usr/bin/env pwsh
kubectl delete namespace eshop
Write-Host "EShop namespace deleted. PVCs also removed." -ForegroundColor Yellow
