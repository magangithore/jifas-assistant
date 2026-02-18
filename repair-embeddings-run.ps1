
# =====================================================
# JIFAS Knowledge Base - Repair Embeddings Script
# =====================================================
# Purpose: Generate embeddings for chunks with NULL embeddings
# =====================================================

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "JIFAS Knowledge Base - Embedding Repair" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$API_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_URL/api/kb/generate-embeddings"
$TIMEOUT = 300000  # 5 minutes

# First, get stats before repair
Write-Host "[1] Fetching knowledge base statistics..." -ForegroundColor Yellow
try {
    $statsResponse = Invoke-WebRequest -Uri "$API_URL/api/kb/stats" `
        -Method Get `
        -Headers @{"Content-Type"="application/json"} `
        -TimeoutSec 30

    $stats = $statsResponse.Content | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "?? Current Knowledge Base Status:" -ForegroundColor Cyan
    Write-Host "   Total Documents: $($stats.totalDocuments)" -ForegroundColor White
    Write-Host "   Total Chunks: $($stats.totalChunks)" -ForegroundColor White
    Write-Host "   Chunks with Embeddings: $($stats.chunksWithEmbeddings)" -ForegroundColor White
    Write-Host "   Embedding Coverage: $($stats.embeddingCoverage)" -ForegroundColor White
    Write-Host ""
    
    # Calculate missing embeddings
    $nullEmbeddingCount = $stats.totalChunks - $stats.chunksWithEmbeddings
    if ($nullEmbeddingCount -gt 0) {
        Write-Host "??  Found $nullEmbeddingCount chunks with NULL embeddings" -ForegroundColor Yellow
    } else {
        Write-Host "? All chunks already have embeddings!" -ForegroundColor Green
        exit 0
    }
}
catch {
    Write-Host "? Error fetching stats: $_" -ForegroundColor Red
    exit 1
}

# Now run the repair endpoint
Write-Host "[2] Starting embedding generation for NULL chunks..." -ForegroundColor Yellow
Write-Host ""

try {
    $startTime = Get-Date
    Write-Host "? Sending repair request to $KB_ENDPOINT" -ForegroundColor Cyan
    
    $repairResponse = Invoke-WebRequest -Uri $KB_ENDPOINT `
        -Method Post `
        -Headers @{"Content-Type"="application/json"} `
        -TimeoutSec $TIMEOUT
    
    $result = $repairResponse.Content | ConvertFrom-Json
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds
    
    Write-Host ""
    Write-Host "?? Embedding Generation Complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "?? Results:" -ForegroundColor Cyan
    Write-Host "   Total Chunks Processed: $($result.totalChunksProcessed)" -ForegroundColor White
    Write-Host "   Successfully Generated: $($result.successCount)" -ForegroundColor Green
    Write-Host "   Failed: $($result.failedCount)" -ForegroundColor $(if($result.failedCount -gt 0) { "Yellow" } else { "Green" })
    Write-Host "   Success Rate: $($result.successRate)" -ForegroundColor White
    Write-Host "   Duration: $([Math]::Round($duration, 2)) seconds" -ForegroundColor White
    Write-Host ""
    
    if ($result.failedCount -gt 0) {
        Write-Host "??  Failed Chunk IDs: $($result.failedChunkIds -join ', ')" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "[3] Verifying results with new stats..." -ForegroundColor Yellow
    
    # Wait a moment for database to settle
    Start-Sleep -Seconds 2
    
    # Get new stats
    $newStatsResponse = Invoke-WebRequest -Uri "$API_URL/api/kb/stats" `
        -Method Get `
        -Headers @{"Content-Type"="application/json"} `
        -TimeoutSec 30
    
    $newStats = $newStatsResponse.Content | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "? Updated Knowledge Base Status:" -ForegroundColor Cyan
    Write-Host "   Total Documents: $($newStats.totalDocuments)" -ForegroundColor White
    Write-Host "   Total Chunks: $($newStats.totalChunks)" -ForegroundColor White
    Write-Host "   Chunks with Embeddings: $($newStats.chunksWithEmbeddings)" -ForegroundColor Green
    Write-Host "   Embedding Coverage: $($newStats.embeddingCoverage)" -ForegroundColor Green
    Write-Host ""
    
    if ($newStats.embeddingCoverage -eq "100%") {
        Write-Host "?? SUCCESS! All embeddings have been generated!" -ForegroundColor Green
    } else {
        Write-Host "??  Note: Some embeddings still pending. You can run this script again." -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "Repair process completed successfully!" -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Cyan
}
catch {
    Write-Host ""
    Write-Host "? Error during embedding generation:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    # Try to parse error response if available
    if ($_.Exception.Response) {
        try {
            $errorStream = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errorContent = $errorStream.ReadToEnd()
            Write-Host ""
            Write-Host "Error Details:" -ForegroundColor Red
            Write-Host $errorContent -ForegroundColor Red
        }
        catch { }
    }
    
    exit 1
}
