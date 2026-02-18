#!/usr/bin/env pwsh
# ============================================================
# JIFAS Knowledge Base Document Insertion Script v2 (FIXED)
# Auto-handles Tags and JSON encoding
# ============================================================

param(
    [switch]$Verbose = $false
)

# Configuration
$API_BASE_URL = "http://localhost:5000"
$KB_ENDPOINT = "$API_BASE_URL/api/kb/documents"
$KB_FOLDER = "D:\Users\magang.it8\jifas-assistant\Jifas.Assistant\KnowledgeBase"

# Colors for output
$Colors = @{
    Success = 'Green'
    Error = 'Red'
    Warning = 'Yellow'
    Info = 'Cyan'
}

function Write-Status {
    param(
        [string]$Message,
        [string]$Status = "Info"
    )
    $Color = $Colors[$Status]
    Write-Host "[$Status] $Message" -ForegroundColor $Color
}

function Read-FileContent {
    param([string]$FilePath)
    
    try {
        $content = Get-Content -Path $FilePath -Raw -Encoding UTF8 -ErrorAction Stop
        return $content
    }
    catch {
        Write-Status "Failed to read file: $FilePath - $_" "Error"
        return $null
    }
}

function Insert-Document {
    param(
        [string]$Title,
        [string]$Content,
        [string]$Category,
        [string]$FilePath
    )
    
    # Create JSON payload with proper escaping
    $payload = @{
        title = $Title
        content = $Content
        category = $Category
        tags = $Category  # Using category as default tags
    } | ConvertTo-Json -Depth 10 -ErrorAction Stop
    
    try {
        Write-Status "Inserting: $Title (Category: $Category)..." "Info"
        
        $response = Invoke-WebRequest `
            -Uri $KB_ENDPOINT `
            -Method POST `
            -Headers @{ "Content-Type" = "application/json" } `
            -Body $payload `
            -TimeoutSec 30 `
            -ErrorAction Stop
        
        if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 201) {
            Write-Status "? Inserted: $Title" "Success"
            return $true
        }
        else {
            Write-Status "? Failed: $Title (Status: $($response.StatusCode))" "Error"
            return $false
        }
    }
    catch {
        $errorResponse = $_.Exception.Response.Content.ReadAsStringAsync().Result
        Write-Status "? Error inserting $Title : $errorResponse" "Error"
        return $false
    }
}

function Test-API {
    try {
        Write-Status "Testing API connectivity..." "Info"
        $response = Invoke-WebRequest `
            -Uri "$API_BASE_URL/health" `
            -Method GET `
            -TimeoutSec 5 `
            -ErrorAction Stop
        
        if ($response.StatusCode -eq 200) {
            Write-Status "? API is running and healthy!" "Success"
            return $true
        }
        else {
            Write-Status "? API returned status $($response.StatusCode)" "Error"
            return $false
        }
    }
    catch {
        Write-Status "? Cannot connect to API at $API_BASE_URL" "Error"
        return $false
    }
}

function Process-Folder {
    param(
        [string]$FolderPath,
        [string]$FolderCategory
    )
    
    Write-Status "Processing folder: $FolderCategory" "Info"
    
    if (-not (Test-Path -Path $FolderPath)) {
        Write-Status "Folder not found: $FolderPath" "Warning"
        return @{ Success = 0; Error = 0; Total = 0 }
    }
    
    $files = Get-ChildItem -Path $FolderPath -Filter "*.txt" -File
    $successCount = 0
    $errorCount = 0
    
    Write-Status "Found $($files.Count) files in $FolderCategory folder" "Info"
    
    foreach ($file in $files) {
        $content = Read-FileContent -FilePath $file.FullName
        
        if ($content) {
            $fileName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            $title = "$FolderCategory - $fileName"
            
            $success = Insert-Document `
                -Title $title `
                -Content $content `
                -Category $FolderCategory `
                -FilePath $file.FullName
            
            if ($success) {
                $successCount++
            }
            else {
                $errorCount++
            }
            
            # Delay to avoid overwhelming API
            Start-Sleep -Milliseconds 500
        }
        else {
            $errorCount++
        }
    }
    
    return @{
        Success = $successCount
        Error = $errorCount
        Total = $files.Count
    }
}

# ============================================================
# MAIN EXECUTION
# ============================================================

Write-Host ""
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS Knowledge Base Document Insertion Script v2    ?" -ForegroundColor Cyan
Write-Host "?  Fixed for API Compatibility                          ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Step 1: Test API
if (-not (Test-API)) {
    Write-Status "API is not running. Please start the application first." "Error"
    exit 1
}

Write-Host ""

# Step 2: Define folders to process
$foldersToProcess = @(
    @{ Path = "$KB_FOLDER\Receiving"; Category = "Receiving" },
    @{ Path = "$KB_FOLDER\Report"; Category = "Report" },
    @{ Path = "$KB_FOLDER\CashBank"; Category = "CashBank" }
)

# Step 3: Process each folder
$totalResults = @{
    Success = 0
    Error = 0
    Total = 0
}

foreach ($folder in $foldersToProcess) {
    Write-Host ""
    $result = Process-Folder -FolderPath $folder.Path -FolderCategory $folder.Category
    
    $totalResults.Success += $result.Success
    $totalResults.Error += $result.Error
    $totalResults.Total += $result.Total
    
    Write-Status "Completed: $($folder.Category) - Success: $($result.Success), Error: $($result.Error)" "Success"
}

# Step 4: Summary
Write-Host ""
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host "?  INSERTION SUMMARY                                   ?" -ForegroundColor Green
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Green

Write-Status "Total Documents Processed: $($totalResults.Total)" "Info"
Write-Status "Successfully Inserted: $($totalResults.Success)" "Success"
Write-Status "Failed Insertions: $($totalResults.Error)" "Error"

if ($totalResults.Error -eq 0 -and $totalResults.Success -gt 0) {
    Write-Host ""
    Write-Status "? All documents inserted successfully!" "Success"
    Write-Status "Documents are being chunked and embedded by the system..." "Info"
}
else {
    Write-Host ""
    if ($totalResults.Success -eq 0) {
        Write-Status "? No documents were inserted. Check errors above." "Warning"
    }
    else {
        Write-Status "? Some documents failed to insert. Check errors above." "Warning"
    }
}

Write-Host ""
