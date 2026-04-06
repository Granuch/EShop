param(
    [string]$Profile = "sandbox",
    [string]$GatewayBaseUrl = "http://localhost:7000",
    [switch]$SkipComposeUp
)

$ErrorActionPreference = "Stop"

Write-Host "[gateway-verify] profile=$Profile, baseUrl=$GatewayBaseUrl"

if (-not $SkipComposeUp) {
    Write-Host "[gateway-verify] Starting docker compose profile..."
    docker compose --profile $Profile up -d
}

Write-Host "[gateway-verify] Waiting for gateway readiness..."
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$GatewayBaseUrl/health/ready" -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $ready) {
    throw "Gateway /health/ready did not return 200 within timeout."
}

Write-Host "[gateway-verify] Checking liveness endpoint..."
$live = Invoke-WebRequest -Uri "$GatewayBaseUrl/health/live" -UseBasicParsing -TimeoutSec 5
if ($live.StatusCode -ne 200) {
    throw "Gateway /health/live is not healthy. Status=$($live.StatusCode)"
}

Write-Host "[gateway-verify] Checking root endpoint..."
$root = Invoke-WebRequest -Uri "$GatewayBaseUrl/" -UseBasicParsing -TimeoutSec 5
if ($root.StatusCode -ne 200) {
    throw "Gateway root endpoint failed. Status=$($root.StatusCode)"
}

Write-Host "[gateway-verify] Checking Mailpit UI..."
$mailpit = Invoke-WebRequest -Uri "http://localhost:8025" -UseBasicParsing -TimeoutSec 5
if ($mailpit.StatusCode -lt 200 -or $mailpit.StatusCode -ge 400) {
    throw "Mailpit UI is not reachable. Status=$($mailpit.StatusCode)"
}

Write-Host "[gateway-verify] Checking simulated route behavior..."
$simulated = Invoke-WebRequest -Uri "$GatewayBaseUrl/api/v1/orders" -Headers @{ "X-Simulate" = "true" } -UseBasicParsing -TimeoutSec 10
if ($simulated.StatusCode -lt 200 -or $simulated.StatusCode -ge 600) {
    throw "Unexpected simulation response status=$($simulated.StatusCode)"
}

Write-Host "[gateway-verify] Verification completed successfully."
