#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Master script to insert Knowledge Base files into JIFAS database
.DESCRIPTION
    This script:
    1. Builds the application
    2. Starts the API server
    3. Waits for API to be ready
    4. Inserts files from Cashbank, Report, Receiving folders
    5. Provides detailed insertion report
#>

param(
    [string]$BaseUrl = "http://localhost:5001",
    [int]$MaxWaitSeconds = 30,
    [switch]$SkipBuild = $false,
    [switch]$SkipStart = $false,
    [switch]$Verbose = $false
)

# Colors
$colors = @{
    Success = 'Green'
    Error   = 'Red'
    Warning = 'Yellow'
    Info    = 'Cyan'
    Header  = 'Magenta'
}

function Write-Status {
    param([string]$Message, [string]$Type = 'Info')
    $color = $colors[$Type]
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$Type] $Message" -ForegroundColor $color
}

function Wait-ForApi {
    param([string]$Url, [int]$MaxSeconds)
    
    Write-Status "Waiting for API to be ready..." 'Info'
    $startTime = Get-Date
    $timeout = New-TimeSpan -Seconds $MaxSeconds
    
    while ((Get-Date) - $startTime -lt $timeout) {
        try {
            $response = Invoke-RestMethod -Uri "$Url/api/kb/documents" -Method Get -ErrorAction Stop
            Write-Status "? API is ready!" 'Success'
            return $true
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    
    Write-Status "API did not become ready within $MaxSeconds seconds" 'Error'
    return $false
}

function Insert-KbDocuments {
    param([string]$BaseUrl)
    
    Write-Status "Starting knowledge base insertion..." 'Header'
    
    $KBPath = "Jifas.Assistant/KnowledgeBase"
    $folders = @('Cashbank', 'Report', 'Receiving')
    
    $totalFiles = 0
    $successCount = 0
    $errorCount = 0
    $results = @()
    
    foreach ($folder in $folders) {
        $folderPath = Join-Path -Path $KBPath -ChildPath $folder
        
        if (-not (Test-Path $folderPath)) {
            Write-Status "Folder not found: $folderPath" 'Warning'
            continue
        }
        
        Write-Status "Processing: $folder" 'Header'
        
        $files = Get-ChildItem -Path $folderPath -Filter "*.txt" -ErrorAction SilentlyContinue
        
        if ($files.Count -eq 0) {
            Write-Status "No files found in $folder" 'Warning'
            continue
        }
        
        Write-Status "Found $($files.Count) files" 'Info'
        
        foreach ($file in $files) {
            try {
                $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
                
                if ([string]::IsNullOrWhiteSpace($content)) {
                    Write-Status "Skipping empty file: $($file.Name)" 'Warning'
                    continue
                }
                
                $title = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
                $category = @{
                    'Cashbank'  = 'Cash Bank Management'
                    'Report'    = 'Financial Reports'
                    'Receiving' = 'Receiving & Goods'
                }[$folder]
                
                $tags = @($folder)
                if ($file.Name -match 'Budget') { $tags += 'Budget' }
                if ($file.Name -match 'Inquiry') { $tags += 'Inquiry' }
                if ($file.Name -match 'Payment') { $tags += 'Payment' }
                if ($file.Name -match 'Tax') { $tags += 'Tax' }
                if ($file.Name -match 'Approval') { $tags += 'Approval' }
                if ($file.Name -match 'Create') { $tags += 'Create' }
                
                $body = @{
                    Title    = $title
                    Content  = $content
                    Category = $category
                    Tags     = [string]::Join(',', $tags)
                } | ConvertTo-Json -Depth 10
                
                Write-Status "Inserting: $title..." 'Info'
                
                $response = Invoke-RestMethod `
                    -Uri "$BaseUrl/api/kb/documents" `
                    -Method Post `
                    -ContentType "application/json" `
                    -Body $body `
                    -ErrorAction Stop
                
                if ($response.success) {
                    Write-Status "? $title [ID: $($response.documentId)]" 'Success'
                    $successCount++
                    $results += @{ Status = 'Success'; Folder = $folder; File = $file.Name; DocId = $response.documentId }
                }
                else {
                    Write-Status "? Failed: $title - $($response.message)" 'Error'
                    $errorCount++
                    $results += @{ Status = 'Error'; Folder = $folder; File = $file.Name; Error = $response.message }
                }
                
                $totalFiles++
                Start-Sleep -Milliseconds 300
            }
            catch {
                Write-Status "? Error: $($file.Name) - $($_.Exception.Message)" 'Error'
                $errorCount++
                $totalFiles++
                $results += @{ Status = 'Error'; Folder = $folder; File = $file.Name; Error = $_.Exception.Message }
            }
        }
    }
    
    return @{ Total = $totalFiles; Success = $successCount; Error = $errorCount; Results = $results }
}

# Main execution
Write-Status "================================================" 'Header'
Write-Status "JIFAS Knowledge Base Bulk Insert" 'Header'
Write-Status "================================================" 'Header'

# Check if API is already running
Write-Status "Checking if API is already running..." 'Info'
try {
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/kb/documents" -Method Get -ErrorAction Stop
    Write-Status "API already running! Skipping start..." 'Success'
    $apiIsRunning = $true
}
catch {
    $apiIsRunning = $false
}

if (-not $apiIsRunning) {
    Write-Status "API is not running. Please start it manually:" 'Warning'
    Write-Status "  cd Jifas.Assistant" 'Info'
    Write-Status "  dotnet run" 'Info'
    Write-Status ""
    Write-Status "Press ENTER when API is running on $BaseUrl..." 'Warning'
    Read-Host
}

# Verify API is ready
if (-not (Wait-ForApi -Url $BaseUrl -MaxSeconds $MaxWaitSeconds)) {
    Write-Status "Could not connect to API at $BaseUrl" 'Error'
    exit 1
}

Write-Status ""

# Run insertion
$insertionResult = Insert-KbDocuments -BaseUrl $BaseUrl

# Print summary
Write-Status ""
Write-Status "================================================" 'Header'
Write-Status "INSERTION SUMMARY" 'Header'
Write-Status "================================================" 'Header'
Write-Status "Total Files: $($insertionResult.Total)" 'Info'
Write-Status "Success: $($insertionResult.Success)" 'Success'
Write-Status "Errors: $($insertionResult.Error)" $(if ($insertionResult.Error -gt 0) { 'Error' } else { 'Success' })
Write-Status ""

# Print detailed results
if ($insertionResult.Results.Count -gt 0) {
    Write-Status "DETAILED RESULTS:" 'Header'
    foreach ($result in $insertionResult.Results) {
        if ($result.Status -eq 'Success') {
            Write-Status "? [$($result.Folder)] $($result.File) [ID: $($result.DocId)]" 'Success'
        }
        else {
            Write-Status "? [$($result.Folder)] $($result.File)" 'Error'
            if ($result.Error) {
                Write-Status "  Error: $($result.Error)" 'Error'
            }
        }
    }
}

Write-Status ""
Write-Status "================================================" 'Header'

if ($insertionResult.Error -eq 0) {
    Write-Status "? All files inserted successfully!" 'Success'
    Write-Status "Total: $($insertionResult.Success) documents inserted" 'Success'
    exit 0
}
else {
    Write-Status "? Insertion completed with $($insertionResult.Error) errors" 'Warning'
    Write-Status "Success: $($insertionResult.Success) / $($insertionResult.Total)" 'Info'
    exit 1
}
