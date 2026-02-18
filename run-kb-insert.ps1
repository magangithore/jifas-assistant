#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simple KB Bulk Insert - Just insert files ke DB via API
.DESCRIPTION
    Insert 24 KB files (Cashbank, Report, Receiving) ke database
    via POST /api/kb/documents endpoint
#>

param(
    [string]$BaseUrl = "http://localhost:5001",
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Continue"

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

Write-Status "================================================" 'Header'
Write-Status "KB BULK INSERT - DIRECT EXECUTION" 'Header'
Write-Status "================================================" 'Header'
Write-Status "Target: $BaseUrl/api/kb/documents" 'Info'
Write-Status ""

# Check if API is running
Write-Status "Checking API..." 'Info'
try {
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/kb/documents" -Method Get -ErrorAction Stop -TimeoutSec 5
    Write-Status "? API is running!" 'Success'
}
catch {
    Write-Status "? API not responding at $BaseUrl" 'Error'
    Write-Status "Make sure to start: cd Jifas.Assistant && dotnet run" 'Warning'
    exit 1
}

Write-Status ""

# Files to insert
$KBPath = "Jifas.Assistant/KnowledgeBase"
$folders = @('Cashbank', 'Report', 'Receiving')

$totalFiles = 0
$successCount = 0
$errorCount = 0

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
            # Read file
            $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
            
            if ([string]::IsNullOrWhiteSpace($content)) {
                Write-Status "Skipping (empty): $($file.Name)" 'Warning'
                continue
            }
            
            # Prepare metadata
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
            
            # Prepare body
            $body = @{
                Title    = $title
                Content  = $content
                Category = $category
                Tags     = [string]::Join(',', $tags)
            } | ConvertTo-Json -Depth 10
            
            Write-Status "Inserting: $title" 'Info'
            
            # Insert via API
            $response = Invoke-RestMethod `
                -Uri "$BaseUrl/api/kb/documents" `
                -Method Post `
                -ContentType "application/json" `
                -Body $body `
                -ErrorAction Stop
            
            if ($response.success) {
                Write-Status "? $title [ID: $($response.documentId)]" 'Success'
                $successCount++
            }
            else {
                Write-Status "? Failed: $title - $($response.message)" 'Error'
                $errorCount++
            }
            
            $totalFiles++
            Start-Sleep -Milliseconds 300
        }
        catch {
            Write-Status "? Error: $($file.Name) - $($_.Exception.Message)" 'Error'
            $errorCount++
            $totalFiles++
        }
    }
}

Write-Status ""
Write-Status "================================================" 'Header'
Write-Status "SUMMARY" 'Header'
Write-Status "================================================" 'Header'
Write-Status "Total Files: $totalFiles" 'Info'
Write-Status "Success: $successCount" 'Success'
Write-Status "Errors: $errorCount" $(if ($errorCount -gt 0) { 'Error' } else { 'Success' })
Write-Status ""

if ($errorCount -eq 0 -and $successCount -gt 0) {
    Write-Status "? All files inserted successfully!" 'Success'
    Write-Status "Check database: SELECT COUNT(*) FROM KnowledgeBaseDocuments" 'Success'
    exit 0
}
else {
    Write-Status "? Process completed with issues" 'Warning'
    exit 1
}
