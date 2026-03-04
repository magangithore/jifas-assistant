#!/usr/bin/env pwsh
# ============================================================
# JIFAS KB - INSERT ALL FILES FROM ALL FOLDERS
# Scans all subdirectories in KnowledgeBase folder
# ============================================================

Write-Host ""
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  KB Document Insertion - ALL FOLDERS                 ?" -ForegroundColor Cyan
Write-Host "?  Scanning & Inserting All .txt Files                 ?" -ForegroundColor Cyan
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Configuration
$KB_ROOT_PATH = "D:\Users\magang.it8\jifas-assistant\KnowledgeBase"
$API_BASE_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_BASE_URL/api/kb/documents"

# ============================================================
# STEP 1: TEST API
# ============================================================
Write-Host "[STEP 1] Testing API Connection..." -ForegroundColor Yellow
try {
    $health = Invoke-WebRequest -Uri "$API_BASE_URL/health" -Method GET -TimeoutSec 5 -ErrorAction Stop
    Write-Host "? API is running!" -ForegroundColor Green
} catch {
    Write-Host "? API not accessible. Start the application first!" -ForegroundColor Red
    exit 1
}

Write-Host ""

# ============================================================
# STEP 2: SCAN ALL FOLDERS
# ============================================================
Write-Host "[STEP 2] Scanning KnowledgeBase folders..." -ForegroundColor Yellow

if (!(Test-Path $KB_ROOT_PATH)) {
    Write-Host "? KnowledgeBase folder not found: $KB_ROOT_PATH" -ForegroundColor Red
    exit 1
}

# Get all subdirectories
$subfolders = Get-ChildItem -Path $KB_ROOT_PATH -Directory -ErrorAction SilentlyContinue

if ($subfolders.Count -eq 0) {
    Write-Host "? No subdirectories found in $KB_ROOT_PATH" -ForegroundColor Red
    exit 1
}

Write-Host "? Found $($subfolders.Count) subdirectories" -ForegroundColor Green

Write-Host ""

# ============================================================
# STEP 3: COUNT TOTAL FILES
# ============================================================
Write-Host "[STEP 3] Counting files..." -ForegroundColor Yellow

$totalFiles = 0
$filesByFolder = @{}

foreach ($folder in $subfolders) {
    $files = Get-ChildItem -Path $folder.FullName -Filter "*.txt" -ErrorAction SilentlyContinue
    $fileCount = $files.Count
    if ($fileCount -eq 0) { $fileCount = 0 }
    $filesByFolder[$folder.Name] = @{
        Path = $folder.FullName
        Count = $fileCount
        Files = $files
    }
    $totalFiles += $fileCount
}

Write-Host "? Found $totalFiles .txt files total" -ForegroundColor Green
Write-Host ""

# Show breakdown
foreach ($folderName in ($filesByFolder.Keys | Sort-Object)) {
    $info = $filesByFolder[$folderName]
    Write-Host "  ?? $($folderName): $($info.Count) files" -ForegroundColor Cyan
}

Write-Host ""

# ============================================================
# STEP 4: INSERT ALL FILES
# ============================================================
Write-Host "[STEP 4] Inserting documents..." -ForegroundColor Yellow
Write-Host ""

$processedCount = 0
$successCount = 0
$failedCount = 0
$failedFiles = @()

foreach ($folderName in ($filesByFolder.Keys | Sort-Object)) {
    $folderInfo = $filesByFolder[$folderName]
    $files = $folderInfo.Files
    
    if ($folderInfo.Count -eq 0) {
        continue
    }
    
    Write-Host "?? Processing folder: $folderName ($($folderInfo.Count) files)" -ForegroundColor Cyan
    Write-Host "??????????????????????????????????????????????????" -ForegroundColor Gray
    
    $folderSuccessCount = 0
    
    foreach ($file in $files) {
        $processedCount++
        
        try {
            # Read file content with UTF-8 encoding
            $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
            
            if ([string]::IsNullOrWhiteSpace($content)) {
                Write-Host "  ??  SKIP: $($file.Name) (empty file)" -ForegroundColor Yellow
                $failedCount++
                $failedFiles += $file.Name
                continue
            }
            
            # Create title from filename
            $title = $file.BaseName
            
            # Prepare JSON payload
            $body = @{
                title    = $title
                content  = $content
                category = $folderName
                tags     = $folderName
            } | ConvertTo-Json -Depth 10
            
            # Encode as UTF-8
            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            
            # Send request
            $response = Invoke-WebRequest `
                -Uri $KB_ENDPOINT `
                -Method POST `
                -ContentType "application/json; charset=utf-8" `
                -Body $bodyBytes `
                -TimeoutSec 60 `
                -ErrorAction Stop
            
            $result = $response.Content | ConvertFrom-Json
            
            if ($result.success) {
                Write-Host "  ? $($file.Name)" -ForegroundColor Green
                $successCount++
                $folderSuccessCount++
            } else {
                Write-Host "  ? $($file.Name) - $($result.message)" -ForegroundColor Red
                $failedCount++
                $failedFiles += $file.Name
            }
        }
        catch {
            Write-Host "  ? $($file.Name) - Error: $($_.Exception.Message)" -ForegroundColor Red
            $failedCount++
            $failedFiles += $file.Name
        }
        
        # Progress indicator
        $progress = [math]::Round(($processedCount / $totalFiles) * 100, 1)
        Write-Progress -Activity "Processing files" -Status "$progress% complete" -PercentComplete $progress
    }
    
    Write-Host "  ? Folder summary: $folderSuccessCount/$($folderInfo.Count) inserted" -ForegroundColor Cyan
    Write-Host ""
}

Write-Progress -Activity "Processing files" -Completed

Write-Host ""

# ============================================================
# STEP 5: GET FINAL STATISTICS
# ============================================================
Write-Host "[STEP 5] Fetching final statistics..." -ForegroundColor Yellow

try {
    $statsResponse = Invoke-WebRequest -Uri "$API_BASE_URL/api/kb/stats" `
        -Method Get `
        -Headers @{"Content-Type"="application/json"} `
        -TimeoutSec 30
    
    $stats = $statsResponse.Content | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host "?? FINAL RESULTS" -ForegroundColor Cyan
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Host "Insertion Summary:" -ForegroundColor White
    Write-Host "  Total Files Processed: $totalFiles" -ForegroundColor White
    Write-Host "  Successfully Inserted: $successCount" -ForegroundColor Green
    Write-Host "  Failed: $failedCount" -ForegroundColor $(if($failedCount -gt 0) { "Red" } else { "Green" })
    
    if ($failedFiles.Count -gt 0) {
        Write-Host "  Failed Files: $($failedFiles -join ', ')" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Database Status:" -ForegroundColor White
    Write-Host "  Total Documents: $($stats.totalDocuments)" -ForegroundColor Cyan
    Write-Host "  Total Chunks: $($stats.totalChunks)" -ForegroundColor Cyan
    Write-Host "  Chunks with Embeddings: $($stats.chunksWithEmbeddings)" -ForegroundColor Green
    Write-Host "  Chunks NULL Embeddings: $($stats.totalChunks - $stats.chunksWithEmbeddings)" -ForegroundColor Yellow
    Write-Host "  Embedding Coverage: $($stats.embeddingCoverage)" -ForegroundColor $(if($stats.embeddingCoverage -eq "100%") { "Green" } else { "Yellow" })
    
    Write-Host ""
    
    if ($stats.embeddingCoverage -eq "100%") {
        Write-Host "?? PERFECT! All embeddings generated automatically!" -ForegroundColor Green
    } elseif ([int]$stats.embeddingCoverage.Replace("%", "") -gt 0) {
        Write-Host "??  Some chunks still need embeddings. Running repair..." -ForegroundColor Yellow
        Write-Host "   Execute: .\repair-embeddings-run.ps1" -ForegroundColor Cyan
    } else {
        Write-Host "??  No embeddings generated. Running repair..." -ForegroundColor Yellow
        Write-Host "   Execute: .\repair-embeddings-run.ps1" -ForegroundColor Cyan
    }
    
    Write-Host ""
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
    Write-Host "? INSERTION COMPLETE!" -ForegroundColor Green
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "??  Could not fetch final statistics: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "But insertion process is complete." -ForegroundColor Green
}
