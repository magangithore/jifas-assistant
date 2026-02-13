# ============================================================================
# JIFAS Knowledge Base - Populate Embeddings with Gemini API
# Generates embeddings for all chunks using text-embedding-004 model
# ============================================================================

param(
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$Database = "JIFAS_Assistant",
    [string]$GeminiApiKey = "Replace with your API Key"
)

$ErrorActionPreference = "Continue"

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  Populating Embeddings with Gemini API              ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

# ============================================================================
# Test Gemini API dengan model yang BENAR
# ============================================================================

Write-Host "`n[Step 1] Testing Gemini Embedding API..." -ForegroundColor Cyan
$testUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key=$GeminiApiKey"

$testPayload = @{
    model = "models/gemini-embedding-001"
    content = @{
        parts = @(@{ text = "This is a test" })
    }
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-WebRequest `
        -Uri $testUrl `
        -Method Post `
        -Body $testPayload `
        -ContentType "application/json" `
        -TimeoutSec 15 `
        -ErrorAction Stop
    
    $result = $response.Content | ConvertFrom-Json
    
    if ($result.embedding -and $result.embedding.values) {
        Write-Host "? Gemini Embedding API is working!" -ForegroundColor Green
        Write-Host "   Endpoint: gemini-embedding-001" -ForegroundColor Green
        Write-Host "   Embedding dimensions: $($result.embedding.values.Count)" -ForegroundColor Green
    }
    else {
        Write-Host "??  API responded but structure unexpected" -ForegroundColor Yellow
        Write-Host "   Response: $($response.Content.Substring(0, 200))" -ForegroundColor Gray
    }
}
catch {
    Write-Host "? Gemini API Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Check your API key or internet connection" -ForegroundColor Yellow
    exit 1
}

# ============================================================================
# Connect to Database
# ============================================================================

Write-Host "`n[Step 2] Connecting to database..." -ForegroundColor Cyan
$connString = "Server=$SqlServer;Database=$Database;Integrated Security=true;"
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = $connString

try {
    $conn.Open()
    Write-Host "? Database connected" -ForegroundColor Green
}
catch {
    Write-Host "? Database connection failed: $_" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Get Chunks without Embeddings
# ============================================================================

Write-Host "`n[Step 3] Loading chunks without embeddings..." -ForegroundColor Cyan
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT Id, DocumentId, Content 
FROM KnowledgeBaseChunks 
WHERE Embedding IS NULL OR Embedding = '[]'
ORDER BY DocumentId, ChunkIndex
"@

$adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$table = New-Object System.Data.DataTable
$adapter.Fill($table) | Out-Null

Write-Host "? Found $($table.Rows.Count) chunks needing embeddings" -ForegroundColor Green

if ($table.Rows.Count -eq 0) {
    Write-Host "`n? All chunks already have embeddings!" -ForegroundColor Green
    $conn.Close()
    exit 0
}

# ============================================================================
# Generate Embeddings for Each Chunk
# ============================================================================

Write-Host "`n[Step 4] Generating embeddings..." -ForegroundColor Cyan
Write-Host "This may take several minutes..." -ForegroundColor Yellow

$processed = 0
$successful = 0
$failed = 0
$startTime = Get-Date

foreach ($row in $table.Rows) {
    $chunkId = $row['Id']
    $docId = $row['DocumentId']
    $content = $row['Content']
    $processed++
    
    # Get embedding from Gemini
    try {
        $payload = @{
            model = "models/gemini-embedding-001"
            content = @{
                parts = @(@{ text = $content.Substring(0, [Math]::Min(3000, $content.Length)) })
            }
        } | ConvertTo-Json -Depth 10
        
        $response = Invoke-WebRequest `
            -Uri $testUrl `
            -Method Post `
            -Body $payload `
            -ContentType "application/json" `
            -TimeoutSec 15 `
            -ErrorAction Stop
        
        $result = $response.Content | ConvertFrom-Json
        
        if ($result.embedding -and $result.embedding.values) {
            # Convert embedding array to JSON
            $embeddingJson = ConvertTo-Json $result.embedding.values -Compress
            $dimensions = $result.embedding.values.Count
            
            # Update database
            $updateCmd = $conn.CreateCommand()
            $updateCmd.CommandText = @"
UPDATE KnowledgeBaseChunks 
SET Embedding = @Embedding, EmbeddingDimensions = @Dimensions, UpdatedAt = GETUTCDATE()
WHERE Id = @ChunkId
"@
            
            $updateCmd.Parameters.AddWithValue("@ChunkId", $chunkId) | Out-Null
            $updateCmd.Parameters.AddWithValue("@Embedding", $embeddingJson) | Out-Null
            $updateCmd.Parameters.AddWithValue("@Dimensions", $dimensions) | Out-Null
            
            $updateCmd.ExecuteNonQuery() | Out-Null
            $successful++
            
            if ($processed % 50 -eq 0) {
                $elapsed = $(Get-Date) - $startTime
                Write-Host "  [$processed/$($table.Rows.Count)] processed in $($elapsed.TotalSeconds)s" -ForegroundColor Gray
            }
        }
        else {
            $failed++
        }
    }
    catch {
        $failed++
        if ($processed % 100 -eq 0) {
            Write-Host "    Error on chunk $chunkId : $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    
    # Rate limiting (Google allows ~100 req/min for free tier)
    Start-Sleep -Milliseconds 750
}

$duration = $(Get-Date) - $startTime

# ============================================================================
# Verification
# ============================================================================

Write-Host "`n[Step 5] Verification..." -ForegroundColor Cyan

$verifyCmd = $conn.CreateCommand()
$verifyCmd.CommandText = "SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NOT NULL AND Embedding != '[]'"
$embeddingCount = $verifyCmd.ExecuteScalar()

$totalChunks = $conn.CreateCommand()
$totalChunks.CommandText = "SELECT COUNT(*) FROM KnowledgeBaseChunks"
$totalCount = $totalChunks.ExecuteScalar()

$conn.Close()

# ============================================================================
# Summary
# ============================================================================

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                      SUMMARY                         ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

Write-Host "`nResults:" -ForegroundColor Green
Write-Host "  Chunks processed:      $processed" -ForegroundColor Green
Write-Host "  Embeddings generated:  $successful" -ForegroundColor Green
Write-Host "  Failed:                $failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Yellow" })
Write-Host "  Duration:              $([Math]::Round($duration.TotalMinutes, 2)) minutes" -ForegroundColor Green

Write-Host "`nDatabase Status:" -ForegroundColor Green
Write-Host "  Total chunks:          $totalCount" -ForegroundColor Green
Write-Host "  With embeddings:       $embeddingCount" -ForegroundColor Green
Write-Host "  Coverage:              $([Math]::Round(($embeddingCount / $totalCount) * 100, 1))%" -ForegroundColor $(if ($embeddingCount -eq $totalCount) { "Green" } else { "Yellow" })

if ($embeddingCount -eq $totalCount) {
    Write-Host "`n? ALL EMBEDDINGS SUCCESSFULLY POPULATED!" -ForegroundColor Green
    Write-Host "`n   Your Knowledge Base is now FULLY READY for RAG!" -ForegroundColor Green
}
elseif ($embeddingCount -gt 0) {
    Write-Host "`n??  Partial embeddings - $([Math]::Round(100 - (($embeddingCount / $totalCount) * 100), 1))% still pending" -ForegroundColor Yellow
}
else {
    Write-Host "`n? No embeddings were generated" -ForegroundColor Red
}

Write-Host "`n"
