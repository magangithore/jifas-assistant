#!/usr/bin/env pwsh
# ============================================================
# JIFAS Knowledge Base Verification Script
# Purpose: Verify successful insertion and chunking
# ============================================================

param(
    [switch]$Detailed = $false,
    [switch]$CheckEmbeddings = $false
)

$API_BASE_URL = "http://localhost:5000"
$Colors = @{
    Success = 'Green'
    Error = 'Red'
    Warning = 'Yellow'
    Info = 'Cyan'
}

function Write-Status {
    param([string]$Message, [string]$Status = "Info")
    $Color = $Colors[$Status]
    Write-Host "[$Status] $Message" -ForegroundColor $Color
}

function Test-API {
    try {
        Write-Status "Testing API connectivity..." "Info"
        $response = Invoke-WebRequest `
            -Uri "$API_BASE_URL/health" `
            -Method GET `
            -TimeoutSec 5
        
        Write-Status "? API is running!" "Success"
        return $true
    }
    catch {
        Write-Status "? API not accessible at $API_BASE_URL" "Error"
        return $false
    }
}

function Query-Database {
    param(
        [string]$Query,
        [string]$Description
    )
    
    Write-Status "$Description" "Info"
    
    try {
        $result = Invoke-SqlCmd `
            -ServerInstance "(localdb)\MSSQLLocalDB" `
            -Database "JIFAS_Assistant" `
            -Query $Query `
            -ErrorAction Stop
        
        return $result
    }
    catch {
        Write-Status "Database query failed: $_" "Error"
        return $null
    }
}

function Get-DocumentStats {
    $query = @"
    SELECT 
        Category,
        COUNT(*) as DocumentCount,
        SUM(LEN(Content)) as TotalContentSize
    FROM KnowledgeBaseDocuments
    WHERE Category IN ('Receiving', 'Report', 'CashBank')
    GROUP BY Category
    ORDER BY Category
"@
    
    return Query-Database -Query $query -Description "Fetching document statistics..."
}

function Get-ChunkStats {
    $query = @"
    SELECT 
        COUNT(*) as TotalChunks,
        AVG(LEN(Content)) as AvgChunkSize,
        MIN(LEN(Content)) as MinChunkSize,
        MAX(LEN(Content)) as MaxChunkSize,
        SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) as ChunksWithEmbedding
    FROM KnowledgeBaseChunks kbc
    INNER JOIN KnowledgeBaseDocuments kbd ON kbc.DocumentId = kbd.Id
    WHERE kbd.Category IN ('Receiving', 'Report', 'CashBank')
"@
    
    return Query-Database -Query $query -Description "Fetching chunk statistics..."
}

function Get-DocumentList {
    $query = @"
    SELECT 
        Id,
        Title,
        Category,
        LEN(Content) as ContentSize,
        CreatedAt
    FROM KnowledgeBaseDocuments
    WHERE Category IN ('Receiving', 'Report', 'CashBank')
    ORDER BY Category, Title
"@
    
    return Query-Database -Query $query -Description "Fetching document list..."
}

function Show-VisualSummary {
    param(
        $DocStats,
        $ChunkStats
    )
    
    Write-Host ""
    Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host "?  KNOWLEDGE BASE VERIFICATION SUMMARY                  ?" -ForegroundColor Cyan
    Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host ""
    
    if ($DocStats) {
        Write-Host "?? DOCUMENTS INSERTED:" -ForegroundColor Cyan
        $DocStats | Format-Table `
            @{Name="Category"; Expression={$_.Category}; Width=15},
            @{Name="Count"; Expression={$_.DocumentCount}; Width=10},
            @{Name="Size (KB)"; Expression={[math]::Round($_.TotalContentSize/1KB, 2)}; Width=15} `
            -AutoSize
    }
    
    if ($ChunkStats) {
        Write-Host ""
        Write-Host "?? CHUNKS CREATED:" -ForegroundColor Cyan
        Write-Host "  Total Chunks: $($ChunkStats.TotalChunks)" -ForegroundColor Green
        Write-Host "  Avg Chunk Size: $([math]::Round($ChunkStats.AvgChunkSize, 0)) bytes" -ForegroundColor Green
        Write-Host "  Chunks with Embeddings: $($ChunkStats.ChunksWithEmbedding)" -ForegroundColor Green
        
        if ($ChunkStats.ChunksWithEmbedding -eq $ChunkStats.TotalChunks) {
            Write-Host "  ? All chunks have embeddings!" -ForegroundColor Green
        }
        else {
            Write-Host "  ? Embedding generation in progress..." -ForegroundColor Yellow
        }
    }
    
    Write-Host ""
}

function Show-DetailedResults {
    param($DocList)
    
    Write-Host ""
    Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host "DETAILED DOCUMENT LIST" -ForegroundColor Cyan
    Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host ""
    
    if ($DocList) {
        $DocList | Format-Table `
            @{Name="ID"; Expression={$_.Id}; Width=5},
            @{Name="Category"; Expression={$_.Category}; Width=12},
            @{Name="Title"; Expression={$_.Title}; Width=40},
            @{Name="Size (KB)"; Expression={[math]::Round($_.ContentSize/1KB, 2)}; Width=10},
            @{Name="Created"; Expression={$_.CreatedAt}; Width=20} `
            -AutoSize | Out-String | ForEach-Object { Write-Host $_ -ForegroundColor Green }
    }
}

function Check-Embeddings {
    $query = @"
    SELECT TOP 5
        kbc.Id,
        kbd.Title,
        LEN(kbc.Embedding) as EmbeddingSize,
        CASE 
            WHEN kbc.Embedding IS NOT NULL THEN 'Generated'
            ELSE 'Pending'
        END as EmbeddingStatus
    FROM KnowledgeBaseChunks kbc
    INNER JOIN KnowledgeBaseDocuments kbd ON kbc.DocumentId = kbd.Id
    WHERE kbd.Category IN ('Receiving', 'Report', 'CashBank')
    ORDER BY kbc.CreatedAt DESC
"@
    
    Write-Status "Checking embedding status..." "Info"
    $result = Query-Database -Query $query -Description ""
    
    if ($result) {
        Write-Host ""
        Write-Host "?? EMBEDDING STATUS (Latest 5 Chunks):" -ForegroundColor Cyan
        $result | Format-Table `
            @{Name="ChunkID"; Expression={$_.Id}; Width=8},
            @{Name="Document"; Expression={$_.Title}; Width=30},
            @{Name="Status"; Expression={$_.EmbeddingStatus}; Width=12},
            @{Name="Size"; Expression={if($_.EmbeddingSize) { [math]::Round($_.EmbeddingSize/1KB, 2).ToString() + " KB" } else { "N/A" }}; Width=10} `
            -AutoSize
    }
}

# ============================================================
# MAIN EXECUTION
# ============================================================

Write-Host ""
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS Knowledge Base Verification                   ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Step 1: Test API
if (-not (Test-API)) {
    Write-Status "Cannot verify without running API" "Error"
    exit 1
}

Write-Host ""

# Step 2: Get statistics
$docStats = Get-DocumentStats
$chunkStats = Get-ChunkStats

# Step 3: Show summary
Show-VisualSummary -DocStats $docStats -ChunkStats $chunkStats

# Step 4: Detailed results if requested
if ($Detailed) {
    $docList = Get-DocumentList
    Show-DetailedResults -DocList $docList
}

# Step 5: Check embeddings if requested
if ($CheckEmbeddings) {
    Check-Embeddings
}

# Final summary
Write-Host ""
if ($docStats) {
    $totalDocs = ($docStats | Measure-Object -Property DocumentCount -Sum).Sum
    if ($totalDocs -gt 0) {
        Write-Status "? Verification complete! $totalDocs documents found in database." "Success"
    }
    else {
        Write-Status "? No documents found. Check insertion results." "Warning"
    }
}

Write-Host ""
