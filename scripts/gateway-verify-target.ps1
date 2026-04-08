param(
    [string]$Profile = "sandbox",
    [string]$GatewayBaseUrl = "http://localhost:7000",
    [switch]$SkipComposeUp,
    [string]$AccessToken = "",
    [string]$IdentityBaseUrl = "http://localhost:7001",
    [string]$TestUserEmail = "",
    [string]$TestUserPassword = ""
)

$ErrorActionPreference = "Stop"

function Get-HttpStatusCode {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [ValidateSet("GET", "POST")][string]$Method = "GET",
        [hashtable]$Headers = @{},
        [string]$Body = "",
        [int]$TimeoutSec = 10
    )

    $request = @{
        Uri                = $Uri
        Method             = $Method
        Headers            = $Headers
        UseBasicParsing    = $true
        TimeoutSec         = $TimeoutSec
        SkipHttpErrorCheck = $true
    }

    if ($Method -eq "POST") {
        $request.ContentType = "application/json"
        $request.Body = $Body
    }

    $response = Invoke-WebRequest @request
    return [int]$response.StatusCode
}

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

if ([string]::IsNullOrWhiteSpace($AccessToken) -and -not [string]::IsNullOrWhiteSpace($TestUserEmail) -and -not [string]::IsNullOrWhiteSpace($TestUserPassword)) {
    Write-Host "[gateway-verify] Getting access token from Identity..."
    $loginPayload = @{ email = $TestUserEmail; password = $TestUserPassword } | ConvertTo-Json
    $loginStatus = Get-HttpStatusCode -Uri "$IdentityBaseUrl/api/v1/auth/login" -Method "POST" -Body $loginPayload -TimeoutSec 10
    if ($loginStatus -ne 200) {
        throw "Identity login failed while preparing authenticated check. Status=$loginStatus"
    }

    $loginResponse = Invoke-WebRequest -Uri "$IdentityBaseUrl/api/v1/auth/login" -Method Post -UseBasicParsing -TimeoutSec 10 -ContentType "application/json" -Body $loginPayload
    $AccessToken = ($loginResponse.Content | ConvertFrom-Json).accessToken
    if ([string]::IsNullOrWhiteSpace($AccessToken)) {
        throw "Identity login succeeded but access token was not returned."
    }
}

Write-Host "[gateway-verify] Checking simulated route behavior..."
$simulationHeaders = @{ "X-Simulate" = "true" }
if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
    $simulationHeaders["Authorization"] = "Bearer $AccessToken"
}

$simulatedStatus = Get-HttpStatusCode -Uri "$GatewayBaseUrl/api/v1/orders" -Headers $simulationHeaders -TimeoutSec 10

if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    if ($simulatedStatus -ne 401) {
        throw "Expected 401 for protected /api/v1/orders without token, got $simulatedStatus"
    }

    Write-Host "[gateway-verify] Protected route check passed (401 without token)."
}
else {
    if ($simulatedStatus -eq 401 -or $simulatedStatus -eq 403) {
        throw "Authenticated simulated request was rejected. Status=$simulatedStatus"
    }

    Write-Host "[gateway-verify] Authenticated simulated request status=$simulatedStatus"
}

Write-Host "[gateway-verify] Verification completed successfully."
