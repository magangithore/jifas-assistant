#!/usr/bin/env pwsh
# ============================================================
# JIFAS Knowledge Base Insertion - FINAL VERSION
# Properly handles JSON encoding for all file types
# ============================================================

# Configuration
$API_BASE_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_BASE_URL/api/kb/documents"
$KB_FOLDER = "D:\Users\magang.it8\jifas-assistant\Jifas.Assistant\KnowledgeBase"

Write-Host ""
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS Knowledge Base Insertion - FINAL VERSION      ?" -ForegroundColor Cyan
Write-Host "?  Inserting: Receiving, Report, CashBank Modules     ?" -ForegroundColor Cyan
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Counter variables
$successCount = 0
$errorCount = 0
$totalCount = 0

# Test API first
Write-Host "[TEST] Checking API connectivity..." -ForegroundColor Cyan
try {
    $healthTest = Invoke-WebRequest -Uri "$API_BASE_URL/health" -Method GET -TimeoutSec 5 -ErrorAction Stop
    Write-Host "[SUCCESS] API is running and healthy!" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] API not accessible. Make sure app is running!" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Define folders to process
$folders = @(
    @{ Path = "$KB_FOLDER\Receiving"; Category = "Receiving" },
    @{ Path = "$KB_FOLDER\Report"; Category = "Report" },
    @{ Path = "$KB_FOLDER\CashBank"; Category = "CashBank" }
)

# Process each folder
foreach ($folder in $folders) {
    $folderPath = $folder.Path
    $category = $folder.Category
    
    if (-not (Test-Path -Path $folderPath)) {
        Write-Host "[WARNING] Folder not found: $folderPath" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "[INFO] Processing: $category folder" -ForegroundColor Cyan
    
    $files = @(Get-ChildItem -Path $folderPath -Filter "*.txt" -File)
    Write-Host "[INFO] Found $($files.Count) files" -ForegroundColor Cyan
    Write-Host ""
    
    foreach ($file in $files) {
        try {
            # Read file
            $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8 -ErrorAction Stop
            $fileName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            $title = "$category - $fileName"
            
            $totalCount++
            
            # Create payload object
            $payload = @{
                title = $title
                content = $content
                category = $category
                tags = $category
            }
            
            # Convert to JSON
            $json = $payload | ConvertTo-Json -Depth 10
            
            Write-Host "[INSERTING] $title..." -ForegroundColor Cyan
            
            # Make API call
            $response = Invoke-WebRequest `
                -Uri $KB_ENDPOINT `
                -Method POST `
                -Headers @{ "Content-Type" = "application/json; charset=utf-8" } `
                -Body $json `
                -TimeoutSec 30 `
                -ErrorAction Stop
            
            if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 201) {
                Write-Host "[SUCCESS] ? Inserted: $title" -ForegroundColor Green
                $successCount++
            } else {
                Write-Host "[ERROR] ? Failed ($($response.StatusCode)): $title" -ForegroundColor Red
                $errorCount++
            }
            
        } catch {
            Write-Host "[ERROR] ? Failed to insert $title" -ForegroundColor Red
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            $errorCount++
        }
        
        # Small delay between requests
        Start-Sleep -Milliseconds 300
    }
    
    Write-Host ""
}

# Summary
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  INSERTION SUMMARY                                  ?" -ForegroundColor Cyan
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "[RESULT] Total Processed: $totalCount" -ForegroundColor Cyan
Write-Host "[SUCCESS] Successfully Inserted: $successCount" -ForegroundColor Green
Write-Host "[ERROR] Failed: $errorCount" -ForegroundColor Red
Write-Host ""

if ($errorCount -eq 0 -and $successCount -gt 0) {
    Write-Host "[SUCCESS] ? All documents inserted successfully!" -ForegroundColor Green
    Write-Host "[INFO] Documents are being chunked and embedded by Gemini..." -ForegroundColor Cyan
} else {
    Write-Host "[WARNING] Some documents may have failed. Check errors above." -ForegroundColor Yellow
}

Write-Host ""
