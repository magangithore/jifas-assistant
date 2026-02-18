#!/usr/bin/env pwsh
# ============================================================
# JIFAS KB - EMBEDDING GENERATION REPAIR SCRIPT
# Generate embeddings for chunks that have NULL embeddings
# ============================================================

# Configuration
$API_BASE_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_BASE_URL/api/kb/generate-embeddings"

Write-Host ""
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  KB Embedding Generation Repair Script              ?" -ForegroundColor Cyan
Write-Host "?  Generates embeddings for NULL chunks                ?" -ForegroundColor Cyan
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Step 1: Test API
Write-Host "[TEST] Checking if API is running..." -ForegroundColor Cyan
try {
    $health = Invoke-WebRequest -Uri "$API_BASE_URL/health" -Method GET -TimeoutSec 5 -ErrorAction Stop
    Write-Host "[SUCCESS] API is running!" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] API not accessible. Start the application first!" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Call endpoint to generate embeddings
Write-Host "[INFO] Sending request to generate embeddings for NULL chunks..." -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest `
        -Uri $KB_ENDPOINT `
        -Method POST `
        -Headers @{ "Content-Type" = "application/json" } `
        -Body '{}' `
        -TimeoutSec 120 `
        -ErrorAction Stop
    
    $result = $response.Content | ConvertFrom-Json
    
    Write-Host "[SUCCESS] Embeddings generated!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Results:" -ForegroundColor Cyan
    Write-Host "  Total chunks processed: $($result.totalChunks)" -ForegroundColor Green
    Write-Host "  Embeddings generated: $($result.embeddingsGenerated)" -ForegroundColor Green
    Write-Host "  Failed: $($result.failed)" -ForegroundColor Yellow
    Write-Host ""
    
    if ($result.message) {
        Write-Host "Message: $($result.message)" -ForegroundColor Cyan
    }
    
} catch {
    Write-Host "[ERROR] Failed to generate embeddings" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[COMPLETE] Embedding generation repair completed!" -ForegroundColor Green
Write-Host ""
