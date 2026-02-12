# ============================================================================
# JIFAS Knowledge Base - Chunking & Embedding with Gemini API
# Chunks all KB documents and generates embeddings
# ============================================================================

param(
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$Database = "JIFAS_Assistant",
    [string]$GeminiApiKey = "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k",
    [string]$GeminiModel = "models/gemini-1.5-flash",
    [int]$ChunkSize = 500,
    [int]$ChunkOverlap = 50
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Configuration
# ============================================================================

$GeminiUrl = "https://generativelanguage.googleapis.com/v1beta/models/${GeminiModel}:embedContent?key=${GeminiApiKey}"
$MaxRetries = 3
$RetryDelayMs = 1000

Write-Host "`n??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  JIFAS Knowledge Base - Chunking & Embedding with Gemini     ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan

# ============================================================================
# Database Functions
# ============================================================================

function Get-DbConnection {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = "Server=$SqlServer;Database=$Database;Integrated Security=true;"
    try {
        $conn.Open()
        return $conn
    }
    catch {
        Write-Host "? Database connection failed: $_" -ForegroundColor Red
        exit 1
    }
}

function Execute-Query {
    param([System.Data.SqlClient.SqlConnection]$Conn, [string]$Query, [hashtable]$Params = @{})
    $cmd = $Conn.CreateCommand()
    $cmd.CommandText = $Query
    
    foreach ($p in $Params.GetEnumerator()) {
        $cmd.Parameters.AddWithValue($p.Key, $p.Value) | Out-Null
    }
    
    return $cmd.ExecuteScalar()
}

function Execute-QueryReader {
    param([System.Data.SqlClient.SqlConnection]$Conn, [string]$Query)
    $cmd = $Conn.CreateCommand()
    $cmd.CommandText = $Query
    return $cmd.ExecuteReader()
}

function Insert-Chunk {
    param(
        [System.Data.SqlClient.SqlConnection]$Conn,
        [int]$DocumentId,
        [int]$ChunkIndex,
        [string]$Content,
        [string]$EmbeddingJson,
        [int]$TokenCount,
        [int]$StartPos,
        [int]$EndPos
    )
    
    $query = @"
INSERT INTO KnowledgeBaseChunks (DocumentId, ChunkIndex, Content, EmbeddingVector, TokenCount, StartCharPos, EndCharPos, CreatedAt)
VALUES (@DocumentId, @ChunkIndex, @Content, @EmbeddingVector, @TokenCount, @StartPos, @EndPos, GETUTCDATE())
"@
    
    try {
        Execute-Query -Conn $Conn -Query $query -Params @{
            "@DocumentId" = $DocumentId
            "@ChunkIndex" = $ChunkIndex
            "@Content" = $Content
            "@EmbeddingVector" = $EmbeddingJson
            "@TokenCount" = $TokenCount
            "@StartPos" = $StartPos
            "@EndPos" = $EndPos
        } | Out-Null
        return $true
    }
    catch {
        Write-Host "    ? Insert failed: $_" -ForegroundColor Red
        return $false
    }
}

# ============================================================================
# Chunking Functions
# ============================================================================

function Split-IntoChunks {
    param([string]$Content, [int]$ChunkSize = 500, [int]$Overlap = 50)
    
    if ($Content.Length -eq 0) { return @() }
    
    # Split by sentence boundaries
    $sentences = @($Content -split '(?<=[.!?])\s+' | Where-Object { $_ -and $_.Trim().Length -gt 0 })
    
    if ($sentences.Count -eq 0) {
        $sentences = @($Content)
    }
    
    $chunks = @()
    $currentChunk = ""
    $charCount = 0
    $startPos = 0
    
    foreach ($sentence in $sentences) {
        $sentenceLength = $sentence.Length + 1  # +1 for space
        
        if (($charCount + $sentenceLength) -le $ChunkSize) {
            $currentChunk += " " + $sentence
            $charCount += $sentenceLength
        }
        else {
            if ($currentChunk.Length -gt 0) {
                $chunk = $currentChunk.Trim()
                $endPos = $startPos + $chunk.Length
                $chunks += @{
                    Content  = $chunk
                    StartPos = $startPos
                    EndPos   = $endPos
                }
                $startPos = $endPos - $Overlap
            }
            $currentChunk = $sentence
            $charCount = $sentenceLength
        }
    }
    
    if ($currentChunk.Length -gt 0) {
        $chunk = $currentChunk.Trim()
        $endPos = $startPos + $chunk.Length
        $chunks += @{
            Content  = $chunk
            StartPos = $startPos
            EndPos   = $endPos
        }
    }
    
    return @($chunks | Where-Object { $_.Content -and $_.Content.Length -gt 0 })
}

# ============================================================================
# Gemini Embedding Function
# ============================================================================

function Get-GeminiEmbedding {
    param([string]$Text, [int]$Attempt = 1)
    
    if ($Text.Length -eq 0) { return $null }
    
    try {
        $body = @{
            model = $GeminiModel
            content = @{
                parts = @(
                    @{ text = $Text }
                )
            }
        } | ConvertTo-Json -Depth 10 -Compress
        
        $response = Invoke-WebRequest `
            -Uri $GeminiUrl `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -TimeoutSec 30 `
            -ErrorAction Stop
        
        $result = $response.Content | ConvertFrom-Json
        
        if ($result.embedding -and $result.embedding.values) {
            $embedding = [float[]]$result.embedding.values
            return ConvertTo-Json $embedding -Compress
        }
        else {
            Write-Host "    ??  No embedding values in response" -ForegroundColor Yellow
            return "[]"
        }
    }
    catch {
        if ($Attempt -lt $MaxRetries) {
            Write-Host "    ??  Retry $Attempt/$MaxRetries (Error: $($_.Exception.Message.Substring(0,50))...)" -ForegroundColor Yellow
            Start-Sleep -Milliseconds $RetryDelayMs
            return Get-GeminiEmbedding -Text $Text -Attempt ($Attempt + 1)
        }
        else {
            Write-Host "    ? Embedding failed after $MaxRetries retries" -ForegroundColor Yellow
            return "[]"
        }
    }
}

# ============================================================================
# Main Processing
# ============================================================================

# Test database connection
Write-Host "`n[Step 1] Testing database connection..." -ForegroundColor Cyan
$connection = Get-DbConnection
Write-Host "? Database connected successfully" -ForegroundColor Green

# Get document count
Write-Host "`n[Step 2] Reading documents from database..." -ForegroundColor Cyan
$docCount = Execute-Query -Conn $connection -Query "SELECT COUNT(*) FROM KnowledgeBaseDocuments"
Write-Host "? Found $docCount documents" -ForegroundColor Green

# Check existing chunks
$existingChunks = Execute-Query -Conn $connection -Query "SELECT COUNT(*) FROM KnowledgeBaseChunks"
if ($existingChunks -gt 0) {
    Write-Host "??  WARNING: Database already contains $existingChunks chunks" -ForegroundColor Yellow
    $response = Read-Host "Clear existing chunks? (y/n)"
    if ($response -eq 'y') {
        Write-Host "Clearing existing chunks..." -ForegroundColor Yellow
        Execute-Query -Conn $connection -Query "DELETE FROM KnowledgeBaseChunks; DBCC CHECKIDENT ('KnowledgeBaseChunks', RESEED, 0);" | Out-Null
        Write-Host "? Cleared" -ForegroundColor Green
    }
}

# Get all documents first
Write-Host "`n[Step 3] Loading all documents into memory..." -ForegroundColor Cyan
$cmd = $connection.CreateCommand()
$cmd.CommandText = "SELECT Id, Title, Content, Category FROM KnowledgeBaseDocuments ORDER BY Id"
$adapter = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
$table = New-Object System.Data.DataTable
$adapter.Fill($table) | Out-Null

Write-Host "? Loaded $($table.Rows.Count) documents" -ForegroundColor Green

# Start chunking and embedding
Write-Host "`n[Step 4] Starting chunking and embedding..." -ForegroundColor Cyan
Write-Host "Configuration: ChunkSize=$ChunkSize, Overlap=$ChunkOverlap`n" -ForegroundColor Gray

$totalDocs = 0
$totalChunks = 0
$totalEmbeddings = 0
$totalFailed = 0
$startTime = Get-Date

try {
    foreach ($row in $table.Rows) {
        $docId = $row['Id']
        $title = $row['Title']
        $content = $row['Content']
        $category = $row['Category']
        
        Write-Host "Processing: [$docId] $title" -ForegroundColor Cyan
        
        # Split into chunks
        $chunks = Split-IntoChunks -Content $content -ChunkSize $ChunkSize -Overlap $ChunkOverlap
        
        if ($chunks.Count -eq 0) {
            Write-Host "  ??  No chunks generated (content too short?)" -ForegroundColor Yellow
            continue
        }
        
        Write-Host "  Generated $($chunks.Count) chunks" -ForegroundColor Gray
        $docChunkCount = 0
        $docEmbeddingCount = 0
        
        # Process each chunk
        for ($i = 0; $i -lt $chunks.Count; $i++) {
            $chunk = $chunks[$i]
            $chunkText = $chunk.Content
            $tokenCount = @($chunkText.Split()).Length
            
            # Get embedding from Gemini
            $embeddingJson = Get-GeminiEmbedding -Text $chunkText
            
            if ($embeddingJson -ne "[]") {
                $docEmbeddingCount++
                $totalEmbeddings++
            }
            
            # Insert chunk
            $inserted = Insert-Chunk `
                -Conn $connection `
                -DocumentId $docId `
                -ChunkIndex $i `
                -Content $chunkText `
                -EmbeddingJson $embeddingJson `
                -TokenCount $tokenCount `
                -StartPos $chunk.StartPos `
                -EndPos $chunk.EndPos
            
            if ($inserted) {
                $docChunkCount++
                $totalChunks++
            }
            else {
                $totalFailed++
            }
            
            # Progress indicator
            if (($i + 1) % 5 -eq 0) {
                Write-Host "    [$($i+1)/$($chunks.Count)] processed" -ForegroundColor Gray
            }
            
            # Rate limiting for Gemini API
            Start-Sleep -Milliseconds 100
        }
        
        Write-Host "  ? $docChunkCount chunks inserted | $docEmbeddingCount embeddings generated" -ForegroundColor Green
        $totalDocs++
    }
}
catch {
    Write-Host "? Error during chunking: $_" -ForegroundColor Red
}

$duration = $(Get-Date) - $startTime

# ============================================================================
# Summary & Verification
# ============================================================================

Write-Host "`n??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                        PROCESSING SUMMARY                    ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan

Write-Host "`n[Statistics]" -ForegroundColor Cyan
Write-Host "  Documents processed:    $totalDocs" -ForegroundColor Green
Write-Host "  Chunks created:         $totalChunks" -ForegroundColor Green
Write-Host "  Embeddings generated:   $totalEmbeddings" -ForegroundColor Green
Write-Host "  Failed inserts:         $totalFailed" -ForegroundColor $(if ($totalFailed -eq 0) { "Green" } else { "Red" })
Write-Host "  Duration:               $([Math]::Round($duration.TotalMinutes, 2)) minutes" -ForegroundColor Green

# Verification
Write-Host "`n[Database Verification]" -ForegroundColor Cyan

$totalChunksInDb = Execute-Query -Conn $connection -Query "SELECT COUNT(*) FROM KnowledgeBaseChunks"
$chunksWithEmbedding = Execute-Query -Conn $connection -Query "SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE EmbeddingVector != '[]' AND EmbeddingVector IS NOT NULL"

Write-Host "  Total chunks in database:       $totalChunksInDb" -ForegroundColor Green
Write-Host "  Chunks with embeddings:         $chunksWithEmbedding" -ForegroundColor Green
Write-Host "  Chunks without embeddings:      $($totalChunksInDb - $chunksWithEmbedding)" -ForegroundColor $(if ($totalChunksInDb -eq $chunksWithEmbedding) { "Green" } else { "Yellow" })

# Category breakdown
Write-Host "`n[Breakdown by Category]" -ForegroundColor Cyan
$cmd = $connection.CreateCommand()
$cmd.CommandText = @"
SELECT 
    d.Category,
    COUNT(DISTINCT d.Id) as DocCount,
    COUNT(c.Id) as ChunkCount,
    SUM(c.TokenCount) as TotalTokens,
    SUM(CASE WHEN c.EmbeddingVector != '[]' AND c.EmbeddingVector IS NOT NULL THEN 1 ELSE 0 END) as EmbeddingCount
FROM KnowledgeBaseDocuments d
LEFT JOIN KnowledgeBaseChunks c ON d.Id = c.DocumentId
GROUP BY d.Category
ORDER BY d.Category
"@

$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $cat = $reader.GetString(0)
    $docCnt = $reader.GetInt32(1)
    $chunkCnt = $reader.GetInt32(2)
    $tokenCnt = $reader.GetInt32(3)
    $embedCnt = $reader.GetInt32(4)
    Write-Host "  $cat" -ForegroundColor Gray
    Write-Host "    Docs: $docCnt | Chunks: $chunkCnt | Tokens: $tokenCnt | Embeddings: $embedCnt" -ForegroundColor Gray
}

# Final status
Write-Host "`n??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
if ($totalFailed -eq 0 -and $totalChunksInDb -eq $totalChunks) {
    Write-Host "?  ? CHUNKING & EMBEDDING COMPLETED SUCCESSFULLY!            ?" -ForegroundColor Green
    Write-Host "?                                                            ?" -ForegroundColor Green
    Write-Host "?  Your Knowledge Base is now ready for RAG search!         ?" -ForegroundColor Green
}
else {
    Write-Host "?  ??  CHUNKING & EMBEDDING COMPLETED WITH WARNINGS          ?" -ForegroundColor Yellow
}
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan

$connection.Close()
$connection.Dispose()

Write-Host "`n"
