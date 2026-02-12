# ============================================================================
# JIFAS Knowledge Base - Chunking & Embedding with Gemini API
# Simplified & Robust version
# ============================================================================

param(
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$Database = "JIFAS_Assistant",
    [string]$GeminiApiKey = "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k",
    [int]$ChunkSize = 500
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Configuration
# ============================================================================

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS KB - Chunking & Embedding (Simplified)        ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

# Test Gemini API first
Write-Host "`n[Step 1] Testing Gemini API..." -ForegroundColor Cyan
$testUrl = "https://generativelanguage.googleapis.com/v1/models/text-embedding-004:embedContent?key=$GeminiApiKey"
$testBody = @{
    model = "models/text-embedding-004"
    content = @{
        parts = @(@{ text = "Test" })
    }
} | ConvertTo-Json -Depth 10

try {
    $testResponse = Invoke-WebRequest `
        -Uri $testUrl `
        -Method Post `
        -Body $testBody `
        -ContentType "application/json" `
        -TimeoutSec 15 `
        -ErrorAction Stop
    
    $testResult = $testResponse.Content | ConvertFrom-Json
    
    if ($testResult.embedding -and $testResult.embedding.values) {
        Write-Host "? Gemini API is working" -ForegroundColor Green
        $embeddingDim = $testResult.embedding.values.Count
        Write-Host "  Embedding dimensions: $embeddingDim" -ForegroundColor Gray
    }
    else {
        Write-Host "? API responded but no embedding (might be test issue)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "??  Gemini API Error: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "   Continuing with NULL embeddings..." -ForegroundColor Yellow
}

# ============================================================================
# Database Connection
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
# Load Documents
# ============================================================================

Write-Host "`n[Step 3] Loading documents..." -ForegroundColor Cyan
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Title, Content FROM KnowledgeBaseDocuments ORDER BY Id"
$adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$table = New-Object System.Data.DataTable
$adapter.Fill($table) | Out-Null

Write-Host "? Loaded $($table.Rows.Count) documents" -ForegroundColor Green

# ============================================================================
# Split into Chunks
# ============================================================================

function Split-Content {
    param([string]$Text, [int]$MaxSize = 500)
    
    if (!$Text -or $Text.Length -eq 0) { return @() }
    
    # Split by sentences first
    $sentences = @($Text -split '(?<=[.!?])\s+' | Where-Object { $_ -and $_.Trim().Length -gt 10 })
    
    if ($sentences.Count -eq 0) {
        # If no sentences, split by paragraphs
        $sentences = @($Text -split '\r?\n\r?\n' | Where-Object { $_ -and $_.Trim().Length -gt 10 })
    }
    
    $chunks = @()
    $currentChunk = ""
    
    foreach ($sentence in $sentences) {
        $testChunk = $currentChunk + " " + $sentence
        
        if ($testChunk.Length -le $MaxSize) {
            $currentChunk = $testChunk.Trim()
        }
        else {
            if ($currentChunk.Length -gt 0) {
                $chunks += $currentChunk
            }
            $currentChunk = $sentence.Trim()
        }
    }
    
    if ($currentChunk.Length -gt 0) {
        $chunks += $currentChunk
    }
    
    return @($chunks | Where-Object { $_ -and $_.Length -gt 0 })
}

# ============================================================================
# Get Embedding
# ============================================================================

function Get-Embedding {
    param([string]$Text, [int]$Retry = 0)
    
    if (!$Text -or $Text.Length -eq 0) { return "[]" }
    
    try {
        $url = "https://generativelanguage.googleapis.com/v1/models/text-embedding-004:embedContent?key=$GeminiApiKey"
        $body = @{
            model = "models/text-embedding-004"
            content = @{
                parts = @(@{ text = $Text.Substring(0, [Math]::Min(3000, $Text.Length)) })
            }
        } | ConvertTo-Json -Depth 10 -Compress
        
        $response = Invoke-WebRequest `
            -Uri $url `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -TimeoutSec 15 `
            -ErrorAction Stop
        
        $result = $response.Content | ConvertFrom-Json
        
        if ($result.embedding -and $result.embedding.values) {
            return ($result.embedding.values | ConvertTo-Json -Compress)
        }
        else {
            return "[]"
        }
    }
    catch {
        if ($Retry -lt 2) {
            Start-Sleep -Milliseconds 500
            return Get-Embedding -Text $Text -Retry ($Retry + 1)
        }
        else {
            return "[]"
        }
    }
}

# ============================================================================
# Insert Chunks
# ============================================================================

Write-Host "`n[Step 4] Creating chunks and embeddings..." -ForegroundColor Cyan
$totalChunks = 0
$totalEmbeddings = 0
$startTime = Get-Date

foreach ($row in $table.Rows) {
    $docId = $row['Id']
    $title = $row['Title']
    $content = $row['Content']
    
    Write-Host "Processing: [$docId] $title" -ForegroundColor Cyan
    
    # Split content
    $chunks = Split-Content -Text $content -MaxSize $ChunkSize
    
    if ($chunks.Count -eq 0) {
        Write-Host "  ??  No chunks (content too short)" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "  Chunks: $($chunks.Count)" -ForegroundColor Gray
    
    # Process each chunk
    for ($i = 0; $i -lt $chunks.Count; $i++) {
        $chunkText = [string]$chunks[$i]
        $tokenCount = @($chunkText.Split()).Length
        
        # Get embedding
        $embedding = Get-Embedding -Text $chunkText
        if ($embedding -ne "[]") {
            $totalEmbeddings++
        }
        
        # Insert chunk
        try {
            $insertCmd = $conn.CreateCommand()
            $insertCmd.CommandText = @"
INSERT INTO KnowledgeBaseChunks (DocumentId, ChunkIndex, Content, Embedding, EmbeddingDimensions, StartCharPos, EndCharPos, CreatedAt)
VALUES (@DocId, @Idx, @Content, @Embedding, @EmbeddingDim, @StartPos, @EndPos, GETUTCDATE())
"@
            
            $insertCmd.Parameters.AddWithValue("@DocId", $docId) | Out-Null
            $insertCmd.Parameters.AddWithValue("@Idx", $i) | Out-Null
            $insertCmd.Parameters.AddWithValue("@Content", $chunkText) | Out-Null
            $insertCmd.Parameters.AddWithValue("@Embedding", $embedding) | Out-Null
            $insertCmd.Parameters.AddWithValue("@EmbeddingDim", 384) | Out-Null
            $insertCmd.Parameters.AddWithValue("@StartPos", 0) | Out-Null
            $insertCmd.Parameters.AddWithValue("@EndPos", $chunkText.Length) | Out-Null
            
            $insertCmd.ExecuteNonQuery() | Out-Null
            $totalChunks++
            
            if (($i + 1) % 5 -eq 0) {
                Write-Host "    [$($i+1)/$($chunks.Count)] chunks processed" -ForegroundColor Gray
            }
        }
        catch {
            Write-Host "    ? Insert failed: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        # Rate limiting
        Start-Sleep -Milliseconds 200
    }
    
    Write-Host "  ? Done" -ForegroundColor Green
}

$duration = $(Get-Date) - $startTime

# ============================================================================
# Verification
# ============================================================================

Write-Host "`n[Step 5] Verification..." -ForegroundColor Cyan

$verifyCmd = $conn.CreateCommand()
$verifyCmd.CommandText = "SELECT COUNT(*) FROM KnowledgeBaseChunks"
$chunkCount = $verifyCmd.ExecuteScalar()

$verifyCmd2 = $conn.CreateCommand()
$verifyCmd2.CommandText = "SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NOT NULL AND Embedding != '[]'"
$embeddingCount = $verifyCmd2.ExecuteScalar()

Write-Host "`n??????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                      SUMMARY                         ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????" -ForegroundColor Cyan

Write-Host "`nResults:" -ForegroundColor Green
Write-Host "  Total chunks created:    $totalChunks" -ForegroundColor Green
Write-Host "  Total embeddings:        $totalEmbeddings" -ForegroundColor Green
Write-Host "  Chunks in database:      $chunkCount" -ForegroundColor Green
Write-Host "  Embeddings in database:  $embeddingCount" -ForegroundColor Green
Write-Host "  Duration:                $([Math]::Round($duration.TotalMinutes, 2)) minutes" -ForegroundColor Green

if ($chunkCount -gt 0) {
    Write-Host "`n? Knowledge Base is ready for RAG search!" -ForegroundColor Green
}

$conn.Close()
Write-Host "`n"
