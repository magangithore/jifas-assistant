# ============================================
# JIFAS Knowledge Base Seeding Script
# PowerShell Version (More Reliable)
# ============================================

param(
    [string]$AppUrl = "http://localhost:5180",
    [string]$FolderPath = "./knowledge-base"
)

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "JIFAS Knowledge Base Seeding Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Function to check if app is running
function Check-AppRunning {
    param([string]$Url)
    
    try {
        $response = Invoke-WebRequest -Uri "$Url/api/health" -Method GET -TimeoutSec 2 -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

# Function to seed KB
function Seed-KnowledgeBase {
    param([string]$Url, [string]$FolderPath)
    
    try {
        Write-Host "[SEEDING] Calling endpoint: POST $Url/api/kb/admin/seed" -ForegroundColor Yellow
        
        $response = Invoke-WebRequest -Uri "$Url/api/kb/admin/seed" `
            -Method POST `
            -ContentType "application/json" `
            -TimeoutSec 30 `
            -ErrorAction Stop
        
        $content = $response.Content | ConvertFrom-Json
        
        Write-Host "`n[SUCCESS] Seeding completed!" -ForegroundColor Green
        Write-Host "`nResults:" -ForegroundColor Cyan
        
        if ($content.results) {
            $content.results | Format-Table -Property @(
                @{Label="File"; Expression={$_.fileName}},
                @{Label="Status"; Expression={if($_.success){"? SUCCESS"}else{"? FAILED"}}},
                @{Label="Doc ID"; Expression={$_.documentId}},
                @{Label="Message"; Expression={$_.message}}
            ) -AutoSize
            
            $successCount = @($content.results | Where-Object {$_.success}).Count
            $totalCount = $content.results.Count
            
            Write-Host "`nSummary: $successCount/$totalCount files seeded successfully" -ForegroundColor Green
        }
        
        return $true
    }
    catch {
        Write-Host "[ERROR] Seeding failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Function to verify database
function Verify-Database {
    try {
        Write-Host "`n[VERIFYING] Checking database..." -ForegroundColor Yellow
        
        $query = @"
SELECT 
    COUNT(*) as TotalDocuments,
    COUNT(DISTINCT Category) as UniqueCategories,
    SUM(CASE WHEN EmbeddingDimensions > 0 THEN 1 ELSE 0 END) as WithEmbeddings
FROM KnowledgeBaseDocuments
"@
        
        $result = Invoke-Sqlcmd -ServerInstance "localhost" -Database "JIFAS_Assistant" -Query $query -ErrorAction Stop
        
        Write-Host "`n[DATABASE] Results:" -ForegroundColor Cyan
        Write-Host "  Total Documents: $($result.TotalDocuments)" -ForegroundColor Green
        Write-Host "  Unique Categories: $($result.UniqueCategories)" -ForegroundColor Green
        Write-Host "  With Embeddings: $($result.WithEmbeddings)" -ForegroundColor Green
        
        if ($result.TotalDocuments -gt 0) {
            Write-Host "`n[DATABASE] Sample documents:" -ForegroundColor Cyan
            
            $sampleQuery = @"
SELECT TOP 5 
    Id,
    Title,
    Category,
    EmbeddingDimensions,
    CreatedAt
FROM KnowledgeBaseDocuments
ORDER BY Id DESC
"@
            
            $samples = Invoke-Sqlcmd -ServerInstance "localhost" -Database "JIFAS_Assistant" -Query $sampleQuery
            $samples | Format-Table -Property @(
                @{Label="ID"; Expression={$_.Id}},
                @{Label="Title"; Expression={$_.Title}},
                @{Label="Category"; Expression={$_.Category}},
                @{Label="Dims"; Expression={$_.EmbeddingDimensions}},
                @{Label="Created"; Expression={$_.CreatedAt.ToString("yyyy-MM-dd HH:mm")}}
            ) -AutoSize
        }
    }
    catch {
        Write-Host "[ERROR] Database verification failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Make sure SQL Server is running and JIFAS_Assistant database exists" -ForegroundColor Yellow
    }
}

# Main execution
Write-Host "[STEP 1/4] Checking application..." -ForegroundColor Yellow
if (Check-AppRunning -Url $AppUrl) {
    Write-Host "[OK] App is running on $AppUrl" -ForegroundColor Green
}
else {
    Write-Host "[ERROR] App not running on $AppUrl" -ForegroundColor Red
    Write-Host "`nPlease start the application first:" -ForegroundColor Yellow
    Write-Host "  1. Open PowerShell/Terminal" -ForegroundColor Yellow
    Write-Host "  2. Run: cd D:\Users\magang.it8\jifas-assistant" -ForegroundColor Yellow
    Write-Host "  3. Run: dotnet run" -ForegroundColor Yellow
    Write-Host "  4. Wait for 'Now listening on: http://localhost:5180'" -ForegroundColor Yellow
    Write-Host "  5. Then run this script again" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[STEP 2/4] Checking files..." -ForegroundColor Yellow
if (Test-Path $FolderPath) {
    $fileCount = (Get-ChildItem -Path $FolderPath -Recurse -Include "*.txt", "*.md" | Measure-Object).Count
    Write-Host "[OK] Found $fileCount files in $FolderPath" -ForegroundColor Green
}
else {
    Write-Host "[WARNING] Folder $FolderPath not found" -ForegroundColor Yellow
}

Write-Host "`n[STEP 3/4] Seeding knowledge base..." -ForegroundColor Yellow
$seedSuccess = Seed-KnowledgeBase -Url $AppUrl -FolderPath $FolderPath

if ($seedSuccess) {
    Write-Host "`n[STEP 4/4] Verifying database..." -ForegroundColor Yellow
    Verify-Database
}

Write-Host "`n============================================" -ForegroundColor Cyan
Write-Host "SEEDING COMPLETE!" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Check results above ?" -ForegroundColor Yellow
Write-Host "2. Test search:" -ForegroundColor Yellow
Write-Host "   Invoke-WebRequest 'http://localhost:5180/api/kb/search?query=invoice'" -ForegroundColor Cyan
Write-Host "3. Test chat API if needed" -ForegroundColor Yellow
Write-Host "`nDocumentation:" -ForegroundColor Yellow
Write-Host "- See: FINAL_SETUP_GUIDE.md" -ForegroundColor Cyan
Write-Host "- See: KB_SETUP_GUIDE.md" -ForegroundColor Cyan
Write-Host ""
