# JIFAS AI Assistant - Production Deployment Script (Windows)
# Usage: .\deploy-production.ps1

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "JIFAS AI Assistant - Production Deployment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "[1/5] Checking prerequisites..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "? .NET not found. Please install .NET 10 SDK" -ForegroundColor Red
    exit 1
}
Write-Host "? .NET SDK found: $dotnetVersion" -ForegroundColor Green

# Check environment variables
Write-Host ""
Write-Host "[2/5] Verifying configuration..." -ForegroundColor Yellow

if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) {
    Write-Host "? GEMINI_API_KEY environment variable not set" -ForegroundColor Red
    Write-Host "Please set: `$env:GEMINI_API_KEY = 'your-api-key'" -ForegroundColor Yellow
    exit 1
}
Write-Host "? GEMINI_API_KEY configured" -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($env:DATABASE_CONNECTION_STRING)) {
    Write-Host "? DATABASE_CONNECTION_STRING not set, using default" -ForegroundColor Yellow
}
Write-Host "? Configuration verified" -ForegroundColor Green

# Build Release
Write-Host ""
Write-Host "[3/5] Building Release version..." -ForegroundColor Yellow
Push-Location Jifas.Assistant
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Build failed" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "? Build successful" -ForegroundColor Green

# Publish
Write-Host ""
Write-Host "[4/5] Publishing application..." -ForegroundColor Yellow
dotnet publish -c Release -o ./publish-prod
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Publish failed" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "? Publish successful" -ForegroundColor Green

# Verify deployment package
Write-Host ""
Write-Host "[5/5] Verifying deployment package..." -ForegroundColor Yellow
if (Test-Path "./publish-prod/Jifas.Assistant.dll") {
    Write-Host "? Main assembly found" -ForegroundColor Green
} else {
    Write-Host "? Main assembly not found" -ForegroundColor Red
    Pop-Location
    exit 1
}

$dllSize = (Get-Item "./publish-prod/Jifas.Assistant.dll").Length / 1MB
Write-Host "? Assembly size: $([Math]::Round($dllSize, 2)) MB" -ForegroundColor Green

Pop-Location

Write-Host ""
Write-Host "=========================================" -ForegroundColor Green
Write-Host "? Deployment Ready!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Deployment package: ./Jifas.Assistant/publish-prod" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy publish-prod folder to server"
Write-Host "2. Set environment variables:"
Write-Host "   - GEMINI_API_KEY = 'your-key'"
Write-Host "   - DATABASE_CONNECTION_STRING = 'your-connection' (if needed)"
Write-Host "3. Run:"
Write-Host "   dotnet Jifas.Assistant.dll"
Write-Host ""
Write-Host "Or with IIS:" -ForegroundColor Yellow
Write-Host "1. Create Application Pool (.NET, Integrated)"
Write-Host "2. Create Website pointing to publish-prod"
Write-Host "3. Configure HTTPS binding"
Write-Host "4. Set app pool Identity: ApplicationPoolIdentity"
Write-Host ""
