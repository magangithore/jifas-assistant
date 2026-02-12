param(
    [string]$KBPath = "D:\Users\magang.it8\jifas-assistant\Jifas.Assistant\KnowledgeBase",
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$Database = "JIFAS_Assistant"
)

# ============================================
# JIFAS KB Auto Seeding Script
# Read files dari knowledge-base folder
# Auto-generate & execute SQL INSERT
# ============================================

Write-Host "?? JIFAS Knowledge Base Auto-Seeding" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host ""

# Check folder exists
if (-not (Test-Path $KBPath)) {
    Write-Host "? Knowledge base folder not found: $KBPath" -ForegroundColor Red
    exit 1
}

Write-Host "? KB Folder: $KBPath" -ForegroundColor Green
Write-Host "? SQL Server: $SqlServer" -ForegroundColor Green
Write-Host "? Database: $Database" -ForegroundColor Green
Write-Host ""

# Get all .txt files
$files = Get-ChildItem -Path $KBPath -Filter "*.txt" -Recurse | Sort-Object FullName

Write-Host "Found $($files.Count) files to seed" -ForegroundColor Cyan
Write-Host ""

# Category mapping
function Get-Category {
    param([string]$FilePath)
    
    $parentFolder = Split-Path -Parent $FilePath | Split-Path -Leaf
    
    switch ($parentFolder) {
        "Master" { return "Master Data" }
        "Invoice" { return "Invoice" }
        "PUM" { return "PUM" }
        "Payment" { return "Payment" }
        "OverBudget" { return "Over Budget" }
        default { return "General" }
    }
}

# Tags mapping
function Get-Tags {
    param(
        [string]$Title,
        [string]$Category
    )
    
    $tags = "jifas,kb," + $Category.ToLower().Replace(" ", "")
    
    if ($Title.ToLower() -contains "invoice") { $tags += ",invoice" }
    if ($Title.ToLower() -contains "payment") { $tags += ",payment" }
    if ($Title.ToLower() -contains "budget") { $tags += ",budget" }
    if ($Title.ToLower() -contains "approval") { $tags += ",approval" }
    
    return $tags
}

# Escape SQL string
function Escape-SqlString {
    param([string]$str)
    return $str.Replace("'", "''")
}

# Connection string
$connectionString = "Server=$SqlServer;Database=$Database;Integrated Security=true;Encrypt=false;"

try {
    # Test connection
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.Open()
    Write-Host "? Database connection successful" -ForegroundColor Green
    $connection.Close()
}
catch {
    Write-Host "? Database connection failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Starting insert..." -ForegroundColor Yellow
Write-Host ""

$successCount = 0
$failureCount = 0

# Process each file
foreach ($file in $files) {
    $fileName = $file.Name
    $title = [System.IO.Path]::GetFileNameWithoutExtension($fileName)
    $category = Get-Category -FilePath $file.FullName
    $tags = Get-Tags -Title $title -Category $category
    
    try {
        # Read file content
        $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
        
        if ([string]::IsNullOrWhiteSpace($content)) {
            Write-Host "  ??  $fileName - Empty file, skipping" -ForegroundColor Yellow
            $failureCount++
            continue
        }
        
        # Escape content for SQL
        $escapedContent = Escape-SqlString -str $content
        
        # Build SQL INSERT statement
        $sql = @"
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    '$title',
    '$escapedContent',
    '$category',
    '$tags',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);
"@
        
        # Execute insert
        $connection = New-Object System.Data.SqlClient.SqlConnection
        $connection.ConnectionString = $connectionString
        $connection.Open()
        
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $command.ExecuteNonQuery() | Out-Null
        
        $connection.Close()
        
        Write-Host "  ? $fileName ($category)" -ForegroundColor Green
        $successCount++
    }
    catch {
        Write-Host "  ? $fileName - Error: $_" -ForegroundColor Red
        $failureCount++
    }
}

Write-Host ""
Write-Host "?? Results:" -ForegroundColor Cyan
Write-Host "==========" -ForegroundColor Cyan
Write-Host "Total files:   $($files.Count)"
Write-Host "Success:       $successCount" -ForegroundColor Green
Write-Host "Failed:        $failureCount" -ForegroundColor $(if ($failureCount -gt 0) { "Red" } else { "Green" })
Write-Host ""

# Verify
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT COUNT(*) FROM KnowledgeBaseDocuments;"
    $docCount = $command.ExecuteScalar()
    
    $command.CommandText = "SELECT Category, COUNT(*) as Count FROM KnowledgeBaseDocuments GROUP BY Category ORDER BY Count DESC;"
    $reader = $command.ExecuteReader()
    
    Write-Host "? Database verification:" -ForegroundColor Cyan
    Write-Host "  Total documents in DB: $docCount" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  By category:" -ForegroundColor Cyan
    while ($reader.Read()) {
        $category = $reader["Category"]
        $count = $reader["Count"]
        Write-Host "    $category : $count" -ForegroundColor Cyan
    }
    
    $connection.Close()
}
catch {
    Write-Host "??  Verification failed: $_" -ForegroundColor Yellow
}

Write-Host ""
if ($successCount -eq $files.Count) {
    Write-Host "? All files inserted successfully!" -ForegroundColor Green
} else {
    Write-Host "??  Some files failed to insert. Please check errors above." -ForegroundColor Yellow
}
