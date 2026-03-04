#!/usr/bin/env pwsh

# JIFAS Knowledge Base Loader - Console App
# Direct insertion to SQL Server without API

Write-Host ""
Write-Host "??????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?   JIFAS Knowledge Base Loader - Start             ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "KBLoader/KBLoader.csproj")) {
    Write-Host "Error: Please run this script from the project root directory" -ForegroundColor Red
    Write-Host "Expected: KBLoader/KBLoader.csproj" -ForegroundColor Red
    exit 1
}

Write-Host "Prerequisites Check:" -ForegroundColor Yellow
Write-Host "  ? SQL Server running with JIFAS_Assistant database" -ForegroundColor Gray
Write-Host "  ? Ollama running at http://10.0.12.54:11434" -ForegroundColor Gray
Write-Host "  ? Both configured in appsettings.json" -ForegroundColor Gray
Write-Host ""

Write-Host "Starting KB Loader..." -ForegroundColor Green
Write-Host ""

Push-Location KBLoader
dotnet run --project KBLoader.csproj --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "? KB Loader failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location

Write-Host ""
Write-Host "? KB Loader completed successfully!" -ForegroundColor Green
Write-Host ""
