#!/usr/bin/env pwsh
# ============================================================
# JIFAS Knowledge Base Document Insertion Script
# Folder: Receiving (CashBank, Report, Receiving)
# Purpose: Insert KB documents via API endpoint
# ============================================================

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

# ============================================================
# FUNCTIONS
# ============================================================

function Write-Status {
    param(
        [string]$Message,
        [string]$Status = "Info"
    )
    $Color = $Colors[$Status]
    Write-Host "[$Status] $Message" -ForegroundColor $Color
}

function Get-FileMetadata {
    param([string]$FilePath)
    
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)
    $folderName = Split-Path $FilePath -Parent | Split-Path -Leaf
    
    return @{
        FileName = $fileName
        FolderName = $folderName
    }
}

function Read-FileContent {
    param([string]$FilePath)
    
    try {
        $content = Get-Content -Path $FilePath -Raw -Encoding UTF8
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
    
    $payload = @{
        title = $Title
        content = $Content
        category = $Category
        filePath = $FilePath
    } | ConvertTo-Json -Depth 10
    
    try {
        Write-Status "Inserting: $Title (Category: $Category)..." "Info"
        
        $response = Invoke-WebRequest `
            -Uri $KB_ENDPOINT `
            -Method POST `
            -Headers @{ "Content-Type" = "application/json" } `
            -Body $payload `
            -TimeoutSec 30
        
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
        Write-Status "? Error inserting $Title : $_" "Error"
        return $false
    }
}

function Test-API {
    try {
        Write-Status "Testing API connectivity..." "Info"
        $response = Invoke-WebRequest `
            -Uri "$API_BASE_URL/health" `
            -Method GET `
            -TimeoutSec 5
        
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
        Write-Status "? Cannot connect to API at $API_BASE_URL : $_" "Error"
        return $false
    }
}

function Process-Folder {
    param(
        [string]$FolderPath,
        [string]$FolderCategory
    )
    
    Write-Status "Processing folder: $FolderCategory" "Info"
    
    $files = Get-ChildItem -Path $FolderPath -Filter "*.txt" -File
    $successCount = 0
    $errorCount = 0
    
    foreach ($file in $files) {
        $content = Read-FileContent -FilePath $file.FullName
        
        if ($content) {
            $metadata = Get-FileMetadata -FilePath $file.FullName
            $title = "$($metadata.FolderName) - $($metadata.FileName)"
            
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

Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS Knowledge Base Document Insertion Script      ?" -ForegroundColor Cyan
Write-Host "?  Insert from: Receiving, Report, CashBank Folders    ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Step 1: Test API
if (-not (Test-API)) {
    Write-Status "API is not running. Please start the application first." "Error"
    Write-Status "Run: dotnet run --project Jifas.Assistant" "Warning"
    exit 1
}

Write-Host ""

# Step 2: Define folders to process
$foldersToProcess = @(
    @{
        Path = "$KB_FOLDER\Receiving"
        Category = "Receiving"
        Description = "Receiving/GR Module"
    },
    @{
        Path = "$KB_FOLDER\Report"
        Category = "Report"
        Description = "Report Module"
    },
    @{
        Path = "$KB_FOLDER\CashBank"
        Category = "CashBank"
        Description = "CashBank Module"
    }
)

# Step 3: Process each folder
$totalResults = @{
    Success = 0
    Error = 0
    Total = 0
}

foreach ($folder in $foldersToProcess) {
    if (Test-Path -Path $folder.Path) {
        Write-Status "Starting: $($folder.Description) ($($folder.Path))" "Info"
        
        $result = Process-Folder -FolderPath $folder.Path -FolderCategory $folder.Category
        
        $totalResults.Success += $result.Success
        $totalResults.Error += $result.Error
        $totalResults.Total += $result.Total
        
        Write-Status "Completed: $($folder.Category) - Success: $($result.Success), Error: $($result.Error)" "Success"
        Write-Host ""
    }
    else {
        Write-Status "Folder not found: $($folder.Path)" "Warning"
    }
}

# Step 4: Summary
Write-Host ""
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host "?  INSERTION SUMMARY                                   ?" -ForegroundColor Green
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Green

Write-Status "Total Documents Processed: $($totalResults.Total)" "Info"
Write-Status "Successfully Inserted: $($totalResults.Success)" "Success"
Write-Status "Failed Insertions: $($totalResults.Error)" "Error"

if ($totalResults.Error -eq 0) {
    Write-Host ""
    Write-Status "? All documents inserted successfully!" "Success"
    Write-Status "Documents are being chunked by Gemini API..." "Info"
    Write-Status "Check knowledge base in database: SELECT * FROM KnowledgeBaseDocuments" "Info"
}
else {
    Write-Host ""
    Write-Status "? Some documents failed to insert. Check logs above." "Warning"
}

Write-Host ""
