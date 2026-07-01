# JIFAS - Clean Deploy Script
# Publish langsung ke root publish/ (Dockerfile COPY ./publish .)
param(
    [switch]$SkipDocker,
    [switch]$SkipHealth
)

$ErrorActionPreference = "Stop"
$root = "D:\Users\magang.it8\jifas-assistant"

Write-Host "=== JIFAS Deploy ===" -ForegroundColor Cyan

# 1. Bersihkan publish folder lama
Write-Host "[1/5] Membersihkan publish folder..." -ForegroundColor Yellow
Remove-Item "$root\publish\*" -Recurse -Force -ErrorAction SilentlyContinue

# 2. Publish ke root
Write-Host "[2/5] dotnet publish..." -ForegroundColor Yellow
dotnet publish "$root\Jifas.Assistant\Jifas.Assistant.csproj" -c Release --no-restore -o "$root\publish"
if ($LASTEXITCODE -ne 0) { Write-Host "PUBLISH GAGAL" -ForegroundColor Red; exit 1 }
Write-Host "OK" -ForegroundColor Green

if (-not $SkipDocker) {
    # 3. Build Docker
    Write-Host "[3/5] Docker build..." -ForegroundColor Yellow
    docker compose --env-file "$root\.env" build jifas-api
    if ($LASTEXITCODE -ne 0) { Write-Host "DOCKER BUILD GAGAL" -ForegroundColor Red; exit 1 }

    # 4. Clear Redis cache
    Write-Host "[4/5] Flush Redis..." -ForegroundColor Yellow
    docker exec jifas-redis redis-cli FLUSHALL 2>$null
    Write-Host "OK" -ForegroundColor Green

    # 5. Start container
    Write-Host "[5/5] Docker up..." -ForegroundColor Yellow
    docker compose --env-file "$root\.env" up -d jifas-api
    if ($LASTEXITCODE -ne 0) { Write-Host "DOCKER UP GAGAL" -ForegroundColor Red; exit 1 }

    # Wait for healthy
    Start-Sleep 8

    if (-not $SkipHealth) {
        $health = Invoke-RestMethod http://localhost:8888/health -TimeoutSec 10
        Write-Host "Health: $($health.status)" -ForegroundColor $(if ($health.status -eq "healthy") { "Green" } else { "Red" })
    }
}

Write-Host "=== Deploy Selesai ===" -ForegroundColor Cyan
