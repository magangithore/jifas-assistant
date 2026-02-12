#!/usr/bin/env powershell
# Deep Test - Try all possible embedding endpoints with Gemini API Key

param(
    [string]$ApiKey = "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k"
)

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?       Deep Testing ALL Embedding Endpoints          ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

$endpoints = @(
    @{ name = "v1/embedding-001"; url = "https://generativelanguage.googleapis.com/v1/models/embedding-001:embedContent?key=$ApiKey"; model = "models/embedding-001" },
    @{ name = "v1beta/embedding-001"; url = "https://generativelanguage.googleapis.com/v1beta/models/embedding-001:embedContent?key=$ApiKey"; model = "models/embedding-001" },
    @{ name = "v1/text-embedding-004"; url = "https://generativelanguage.googleapis.com/v1/models/text-embedding-004:embedContent?key=$ApiKey"; model = "models/text-embedding-004" },
    @{ name = "v1beta/text-embedding-004"; url = "https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent?key=$ApiKey"; model = "models/text-embedding-004" },
    @{ name = "v1/gemini-embedding-001"; url = "https://generativelanguage.googleapis.com/v1/models/gemini-embedding-001:embedContent?key=$ApiKey"; model = "models/gemini-embedding-001" },
    @{ name = "v1beta/gemini-embedding-001"; url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key=$ApiKey"; model = "models/gemini-embedding-001" }
)

$successful = @()
$failed = @()

foreach ($endpoint in $endpoints) {
    Write-Host "`n[Testing] $($endpoint.name)" -ForegroundColor Yellow
    Write-Host "  URL: $($endpoint.url.Split('?')[0])" -ForegroundColor Gray
    
    $payload = @{
        model = $endpoint.model
        content = @{
            parts = @(@{ text = "This is a test for embedding" })
        }
    } | ConvertTo-Json -Depth 10
    
    try {
        $response = Invoke-WebRequest `
            -Uri $endpoint.url `
            -Method Post `
            -Body $payload `
            -ContentType "application/json" `
            -TimeoutSec 10 `
            -ErrorAction Stop
        
        $result = $response.Content | ConvertFrom-Json
        
        if ($result.embedding -and $result.embedding.values) {
            Write-Host "  ? SUCCESS!" -ForegroundColor Green
            Write-Host "     Status: $($response.StatusCode)" -ForegroundColor Green
            Write-Host "     Dimensions: $($result.embedding.values.Count)" -ForegroundColor Green
            $successful += $endpoint.name
        }
        else {
            Write-Host "  ??  Response OK but no embedding data" -ForegroundColor Yellow
            Write-Host "     Response: $($response.Content.Substring(0, 100))" -ForegroundColor Gray
            $failed += $endpoint.name
        }
    }
    catch {
        $errorCode = if ($_.Exception.Response) { $_.Exception.Response.StatusCode.Value__ } else { "Unknown" }
        Write-Host "  ? Failed - Status $errorCode" -ForegroundColor Red
        $failed += "$($endpoint.name) [$errorCode]"
    }
    
    Start-Sleep -Milliseconds 500
}

# Summary
Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                    FINAL SUMMARY                    ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

Write-Host "`n? Successful Endpoints ($($successful.Count)):" -ForegroundColor Green
if ($successful.Count -gt 0) {
    $successful | ForEach-Object { Write-Host "   • $_" -ForegroundColor Green }
}
else {
    Write-Host "   (None)" -ForegroundColor Yellow
}

Write-Host "`n? Failed Endpoints ($($failed.Count)):" -ForegroundColor Red
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "   • $_" -ForegroundColor Red }
}

if ($successful.Count -eq 0) {
    Write-Host "`n??  IMPORTANT INFORMATION:" -ForegroundColor Yellow
    Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Yellow
    Write-Host "Your API Key from Google AI Studio (makersuite.google.com)" -ForegroundColor Yellow
    Write-Host "does NOT support the Embeddings API." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Yellow
    Write-Host "To use embeddings, you need to:" -ForegroundColor Cyan
    Write-Host "  1. Go to https://console.cloud.google.com" -ForegroundColor Cyan
    Write-Host "  2. Create a NEW project" -ForegroundColor Cyan
    Write-Host "  3. Enable billing on that project" -ForegroundColor Cyan
    Write-Host "  4. Enable 'Generative Language API'" -ForegroundColor Cyan
    Write-Host "  5. Create an API key from Cloud Console" -ForegroundColor Cyan
    Write-Host "  6. Use THAT key for embeddings" -ForegroundColor Cyan
    Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Yellow
}

Write-Host "`n"
