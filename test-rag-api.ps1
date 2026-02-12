#!/usr/bin/env powershell
# Test RAG Search API endpoints

$BaseUrl = "http://localhost:5180"
$ApiPath = "/api/knowledgebasesearch"

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?          Testing RAG Search API Endpoints             ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

Start-Sleep -Seconds 2

# Test 1: Health check
Write-Host "`n[Test 1] App Health Check" -ForegroundColor Yellow
try {
    $health = Invoke-WebRequest -Uri "$BaseUrl/" -Method Get -TimeoutSec 10 -SkipCertificateCheck -ErrorAction Stop
    Write-Host "  ? App is responding - Status: $($health.StatusCode)" -ForegroundColor Green
}
catch {
    Write-Host "  ? App not responding yet. Start the app first: dotnet run" -ForegroundColor Red
    Write-Host "     dotnet run --project Jifas.Assistant\Jifas.Assistant.csproj" -ForegroundColor Yellow
    exit 1
}

# Test 2: Keyword Search
Write-Host "`n[Test 2] Keyword Search - /api/knowledgebasesearch/keyword?query=budget&topK=3" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$BaseUrl$ApiPath/keyword?query=budget&topK=3" `
        -Method Get `
        -ContentType "application/json" `
        -TimeoutSec 15 `
        -SkipCertificateCheck `
        -ErrorAction Stop
    
    if ($response.StatusCode -eq 200) {
        $data = $response.Content | ConvertFrom-Json
        Write-Host "  ? SUCCESS - Status: 200" -ForegroundColor Green
        Write-Host "     Query: $($data.query)" -ForegroundColor Green
        Write-Host "     Results found: $($data.resultsCount)" -ForegroundColor Green
        
        if ($data.results.Count -gt 0) {
            Write-Host "     Top result:" -ForegroundColor Green
            Write-Host "       - Document: $($data.results[0].documentTitle)" -ForegroundColor Green
            Write-Host "       - Category: $($data.results[0].documentCategory)" -ForegroundColor Green
            Write-Host "       - Relevance: $([Math]::Round($data.results[0].relevanceScore, 3))" -ForegroundColor Green
            Write-Host "       - Content: $($data.results[0].content.Substring(0, 80))..." -ForegroundColor Green
        }
    }
}
catch {
    Write-Host "  ? FAILED - Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 3: List sample queries
Write-Host "`n[Test 3] Suggested Queries to Try" -ForegroundColor Yellow
Write-Host "  • GET $BaseUrl$ApiPath/keyword?query=invoice&topK=5" -ForegroundColor Cyan
Write-Host "  • GET $BaseUrl$ApiPath/keyword?query=payment&topK=5" -ForegroundColor Cyan
Write-Host "  • GET $BaseUrl$ApiPath/keyword?query=budget&topK=5" -ForegroundColor Cyan

Write-Host "`n[Browser] Open Swagger UI:" -ForegroundColor Yellow
Write-Host "  $BaseUrl/swagger" -ForegroundColor Cyan

Write-Host "`n"
