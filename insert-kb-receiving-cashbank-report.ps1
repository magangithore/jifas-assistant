#!/usr/bin/env pwsh
# ============================================================
# JIFAS KB - INSERT DOCUMENTS FROM 3 FOLDERS
# Folders: Receiving, CashBank, Report
# ============================================================

Write-Host ""
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  KB Document Insertion Script                        ?" -ForegroundColor Cyan
Write-Host "?  Folders: Receiving, CashBank, Report                ?" -ForegroundColor Cyan
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Configuration
$KB_FOLDER_PATH = "D:\Users\magang.it8\jifas-assistant\KnowledgeBase"
$API_BASE_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_BASE_URL/api/kb/documents"

$FOLDERS = @("Receiving", "CashBank", "Report")

# Test API first
Write-Host "[TEST] Checking if API is running..." -ForegroundColor Cyan
try {
    $health = Invoke-WebRequest -Uri "$API_BASE_URL/health" -Method GET -TimeoutSec 5 -ErrorAction Stop
    Write-Host "[SUCCESS] API is running! ?" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] API not accessible. Start the application first!" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Process each folder
$totalDocuments = 0
$totalChunks = 0
$failedDocuments = 0

foreach ($folder in $FOLDERS) {
    $folderPath = Join-Path $KB_FOLDER_PATH $folder
    
    if (!(Test-Path $folderPath)) {
        Write-Host "[WARNING] Folder not found: $folderPath" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host "[FOLDER] Processing: $folder" -ForegroundColor Cyan
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Cyan
    
    $files = Get-ChildItem -Path $folderPath -Filter "*.txt" -ErrorAction SilentlyContinue
    
    if ($files.Count -eq 0) {
        Write-Host "  No .txt files found in this folder" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "  Found $($files.Count) files to process" -ForegroundColor White
    Write-Host ""
    
    $folderDocumentCount = 0
    
    foreach ($file in $files) {
        try {
            # Read file content
            $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
            
            if ([string]::IsNullOrWhiteSpace($content)) {
                Write-Host "  [SKIP] $($file.Name) - Empty file" -ForegroundColor Yellow
                continue
            }
            
            # Create title from filename (remove .txt extension)
            $title = $file.BaseName
            
            # Prepare JSON payload
            $body = @{
                title    = $title
                content  = $content
                category = $folder
                tags     = $folder
            } | ConvertTo-Json -Depth 10
            
            # Ensure UTF-8 encoding
            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            
            # Send to API
            $response = Invoke-WebRequest `
                -Uri $KB_ENDPOINT `
                -Method POST `
                -ContentType "application/json; charset=utf-8" `
                -Body $bodyBytes `
                -TimeoutSec 60 `
                -ErrorAction Stop
            
            $result = $response.Content | ConvertFrom-Json
            
            if ($result.success) {
                Write-Host "  ? $($file.Name) (DocumentId: $($result.documentId))" -ForegroundColor Green
                $totalDocuments++
                $folderDocumentCount++
            } else {
                Write-Host "  ? $($file.Name) - $($result.message)" -ForegroundColor Red
                $failedDocuments++
            }
        }
        catch {
            Write-Host "  ? $($file.Name) - Error: $($_.Exception.Message)" -ForegroundColor Red
            $failedDocuments++
        }
    }
    
    Write-Host ""
    Write-Host "  Summary: $folderDocumentCount documents inserted from $folder" -ForegroundColor Cyan
    Write-Host ""
}

# Get final statistics
Write-Host ""
Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host "[FINAL STATISTICS]" -ForegroundColor Green
Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green

try {
    $statsResponse = Invoke-WebRequest -Uri "$API_BASE_URL/api/kb/stats" `
        -Method Get `
        -Headers @{"Content-Type"="application/json"} `
        -TimeoutSec 30
    
    $stats = $statsResponse.Content | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "Documents Inserted: $totalDocuments" -ForegroundColor Green
    Write-Host "Failed Insertions: $failedDocuments" -ForegroundColor $(if($failedDocuments -gt 0) { "Yellow" } else { "Green" })
    Write-Host ""
    Write-Host "Database Status:" -ForegroundColor Cyan
    Write-Host "  Total Documents: $($stats.totalDocuments)" -ForegroundColor White
    Write-Host "  Total Chunks: $($stats.totalChunks)" -ForegroundColor White
    Write-Host "  Chunks with Embeddings: $($stats.chunksWithEmbeddings)" -ForegroundColor Green
    Write-Host "  Embedding Coverage: $($stats.embeddingCoverage)" -ForegroundColor Green
    Write-Host ""
    
    if ($stats.embeddingCoverage -eq "100%") {
        Write-Host "?? EXCELLENT! All embeddings have been generated automatically!" -ForegroundColor Green
    } elseif ($stats.embeddingCoverage -eq "0%") {
        Write-Host "??  No embeddings generated. Running repair script..." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Execute the following to generate missing embeddings:" -ForegroundColor Yellow
        Write-Host "  .\repair-embeddings-run.ps1" -ForegroundColor Cyan
    } else {
        Write-Host "??  Partial embedding coverage: $($stats.embeddingCoverage)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Could not fetch statistics: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host "Insertion process completed!" -ForegroundColor Green
Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host ""
