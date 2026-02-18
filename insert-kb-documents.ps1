#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Insert Knowledge Base files from Cashbank, Report, and Receiving folders to JIFAS database
.DESCRIPTION
    This script reads all .txt files from the three new KnowledgeBase folders and 
    inserts them via the api/kb/documents endpoint with proper categorization
.AUTHOR
    JIFAS KB System
.DATE
    2025-02-13
#>

param(
    [string]$BaseUrl = "http://localhost:5001",
    [string]$KBPath = "Jifas.Assistant/KnowledgeBase",
    [switch]$Verbose = $false
)

# Colors for output
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
    Write-Host "[$Type] $Message" -ForegroundColor $color
}

function Get-FileCategory {
    param([string]$Folder, [string]$FileName)
    
    # Map folder to category
    $categoryMap = @{
        'Cashbank'  = 'Cash Bank Management'
        'Report'    = 'Financial Reports'
        'Receiving' = 'Receiving & Goods'
    }
    
    return $categoryMap[$Folder]
}

function Get-FileTags {
    param([string]$Folder, [string]$FileName)
    
    $tags = @($Folder)
    
    # Add specific tags based on filename
    if ($FileName -match 'Budget') { $tags += 'Budget' }
    if ($FileName -match 'Inquiry') { $tags += 'Inquiry' }
    if ($FileName -match 'Payment') { $tags += 'Payment' }
    if ($FileName -match 'Tax') { $tags += 'Tax' }
    if ($FileName -match 'Approval') { $tags += 'Approval' }
    if ($FileName -match 'Create') { $tags += 'Create' }
    if ($FileName -match 'Receive') { $tags += 'Receiving' }
    
    return [string]::Join(',', $tags)
}

function Invoke-InsertDocument {
    param(
        [string]$Title,
        [string]$Content,
        [string]$Category,
        [string]$Tags,
        [string]$FilePath
    )
    
    $url = "$BaseUrl/api/kb/documents"
    
    $body = @{
        Title    = $Title
        Content  = $Content
        Category = $Category
        Tags     = $Tags
    } | ConvertTo-Json -Depth 10
    
    try {
        if ($Verbose) {
            Write-Status "Sending request to: $url" 'Info'
            Write-Status "Title: $Title | Category: $Category | Tags: $Tags" 'Info'
        }
        
        $response = Invoke-RestMethod `
            -Uri $url `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -ErrorAction Stop
        
        if ($response.success) {
            Write-Status "? Inserted: $Title (ID: $($response.documentId))" 'Success'
            return @{ Success = $true; DocumentId = $response.documentId }
        }
        else {
            Write-Status "? Failed to insert: $Title - $($response.message)" 'Error'
            return @{ Success = $false; Error = $response.message }
        }
    }
    catch {
        Write-Status "? Error inserting '$Title': $($_.Exception.Message)" 'Error'
        return @{ Success = $false; Error = $_.Exception.Message }
    }
}

# Main execution
Write-Status "===============================================" 'Header'
Write-Status "Knowledge Base Bulk Insert - Cashbank, Report, Receiving" 'Header'
Write-Status "===============================================" 'Header'
Write-Status "Target API: $BaseUrl/api/kb/documents" 'Info'
Write-Status "Knowledge Base Path: $KBPath" 'Info'
Write-Status ""

# Verify folders exist
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
    
    Write-Status "Processing folder: $folder" 'Header'
    
    $files = Get-ChildItem -Path $folderPath -Filter "*.txt" -ErrorAction SilentlyContinue
    $fileCount = $files.Count
    
    if ($fileCount -eq 0) {
        Write-Status "No .txt files found in $folder" 'Warning'
        continue
    }
    
    Write-Status "Found $fileCount files in $folder" 'Info'
    
    foreach ($file in $files) {
        try {
            Write-Status "Reading: $($file.Name)..." 'Info'
            
            # Read file content
            $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8 -ErrorAction Stop
            
            if ([string]::IsNullOrWhiteSpace($content)) {
                Write-Status "File is empty, skipping: $($file.Name)" 'Warning'
                continue
            }
            
            # Prepare metadata
            $title = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            $category = Get-FileCategory -Folder $folder -FileName $file.Name
            $tags = Get-FileTags -Folder $folder -FileName $file.Name
            
            # Insert document
            $result = Invoke-InsertDocument `
                -Title $title `
                -Content $content `
                -Category $category `
                -Tags $tags `
                -FilePath $file.FullName
            
            if ($result.Success) {
                $successCount++
                $results += @{
                    Folder  = $folder
                    File    = $file.Name
                    Status  = 'Success'
                    DocId   = $result.DocumentId
                }
            }
            else {
                $errorCount++
                $results += @{
                    Folder  = $folder
                    File    = $file.Name
                    Status  = 'Error'
                    Error   = $result.Error
                }
            }
            
            $totalFiles++
            
            # Delay between requests to avoid rate limiting
            Start-Sleep -Milliseconds 500
        }
        catch {
            Write-Status "Error processing file '$($file.Name)': $($_.Exception.Message)" 'Error'
            $errorCount++
            $totalFiles++
            
            $results += @{
                Folder  = $folder
                File    = $file.Name
                Status  = 'Error'
                Error   = $_.Exception.Message
            }
        }
    }
    
    Write-Status ""
}

# Summary
Write-Status "===============================================" 'Header'
Write-Status "INSERTION SUMMARY" 'Header'
Write-Status "===============================================" 'Header'
Write-Status "Total Files Processed: $totalFiles" 'Info'
Write-Status "Successfully Inserted: $successCount" 'Success'
Write-Status "Errors: $errorCount" $(if ($errorCount -gt 0) { 'Error' } else { 'Info' })
Write-Status ""

# Detailed results
if ($results.Count -gt 0) {
    Write-Status "DETAILED RESULTS:" 'Header'
    Write-Status ""
    
    foreach ($result in $results) {
        if ($result.Status -eq 'Success') {
            Write-Status "? [$($result.Folder)] $($result.File) [ID: $($result.DocId)]" 'Success'
        }
        else {
            Write-Status "? [$($result.Folder)] $($result.File) - Error: $($result.Error)" 'Error'
        }
    }
}

Write-Status ""
Write-Status "===============================================" 'Header'

if ($errorCount -eq 0) {
    Write-Status "? All files inserted successfully!" 'Success'
    exit 0
}
else {
    Write-Status "? Insertion completed with $errorCount errors" 'Warning'
    exit 1
}
