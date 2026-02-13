#!/usr/bin/env powershell
# Test Gemini API Key - Check what endpoints are working

param(
    [string]$ApiKey = "Replace with your API Key"
)

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?       Testing Gemini API Key Availability            ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

$results = @()

# Test 1: Gemini Chat (generative)
Write-Host "`n[Test 1] Gemini Chat API (generative) - gemini-2.0-flash" -ForegroundColor Yellow
$url1 = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=$ApiKey"
$payload1 = @{
    contents = @(@{
        parts = @(@{ text = "Hello" })
    })
} | ConvertTo-Json -Depth 10

try {
    $resp = Invoke-WebRequest -Uri $url1 -Method Post -Body $payload1 -ContentType "application/json" -TimeoutSec 10 -ErrorAction Stop
    Write-Host "  ? WORKING - Gemini Chat API available" -ForegroundColor Green
    $results += "Chat: OK"
}
catch {
    $status = $_.Exception.Response.StatusCode.Value__
    Write-Host "  ? Status $status - Not available" -ForegroundColor Red
    $results += "Chat: $status"
}

# Test 2: Embeddings with embedding-001
Write-Host "`n[Test 2] Gemini Embeddings API - embedding-001" -ForegroundColor Yellow
$url2 = "https://generativelanguage.googleapis.com/v1beta/models/embedding-001:embedContent?key=$ApiKey"
$payload2 = @{
    model = "models/embedding-001"
    content = @{
        parts = @(@{ text = "Hello" })
    }
} | ConvertTo-Json -Depth 10

try {
    $resp = Invoke-WebRequest -Uri $url2 -Method Post -Body $payload2 -ContentType "application/json" -TimeoutSec 10 -ErrorAction Stop
    Write-Host "  ? WORKING - Embeddings API available" -ForegroundColor Green
    Write-Host "     Dimensions: $($resp.embedding.values.Count)" -ForegroundColor Green
    $results += "Embeddings: OK"
}
catch {
    $status = $_.Exception.Response.StatusCode.Value__
    Write-Host "  ? Status $status - Not available" -ForegroundColor Red
    $results += "Embeddings: $status"
}

# Test 3: v1 endpoint (newer)
Write-Host "`n[Test 3] Gemini Embeddings v1 API - text-embedding-004" -ForegroundColor Yellow
$url3 = "https://generativelanguage.googleapis.com/v1/models/text-embedding-004:embedContent?key=$ApiKey"
$payload3 = @{
    model = "models/text-embedding-004"
    content = @{
        parts = @(@{ text = "Hello" })
    }
} | ConvertTo-Json -Depth 10

try {
    $resp = Invoke-WebRequest -Uri $url3 -Method Post -Body $payload3 -ContentType "application/json" -TimeoutSec 10 -ErrorAction Stop
    Write-Host "  ? WORKING - text-embedding-004 available" -ForegroundColor Green
    $results += "text-embedding-004: OK"
}
catch {
    $status = $_.Exception.Response.StatusCode.Value__
    Write-Host "  ? Status $status - Not available" -ForegroundColor Red
    $results += "text-embedding-004: $status"
}

# Test 4: List available models
Write-Host "`n[Test 4] List Available Models" -ForegroundColor Yellow
$urlModels = "https://generativelanguage.googleapis.com/v1beta/models?key=$ApiKey"

try {
    $resp = Invoke-WebRequest -Uri $urlModels -Method Get -TimeoutSec 10 -ErrorAction Stop
    $data = $resp.Content | ConvertFrom-Json
    
    Write-Host "  ? Got model list:" -ForegroundColor Green
    $data.models | ForEach-Object {
        Write-Host "     � $($_.name)" -ForegroundColor Green
    }
    $results += "Models: OK"
}
catch {
    Write-Host "  ? Cannot list models" -ForegroundColor Red
    $results += "Models: FAILED"
}

# Summary
Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                    SUMMARY                           ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

Write-Host "`nResults:" -ForegroundColor Cyan
$results | ForEach-Object { Write-Host "  � $_" -ForegroundColor Gray }

Write-Host "`nConclusion:" -ForegroundColor Cyan
if ($results -match "Embeddings: OK|text-embedding-004: OK") {
    Write-Host "  ? API Key SUPPORTS embeddings - script should work!" -ForegroundColor Green
}
else {
    Write-Host "  ? API Key doesn't support embeddings - might need:" -ForegroundColor Red
    Write-Host "     1. Different API key with billing enabled" -ForegroundColor Yellow
    Write-Host "     2. Or use alternative service (OpenAI, Cohere, etc)" -ForegroundColor Yellow
}

Write-Host "`n"
