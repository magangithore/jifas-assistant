# ============================================================
# reload-kb.ps1
# Reload KnowledgeBase ke SQL + Chunking + Embedding Ollama
# Model Embedding: qwen3-embedding:4b
# ============================================================

$ErrorActionPreference = "Stop"

# ── CONFIG ──────────────────────────────────────────────────
$ConnStr    = "Server=(localdb)\MSSQLLocalDB;Database=JIFAS_Assistant;Integrated Security=true;Encrypt=false;TrustServerCertificate=true"
$KbRoot     = "D:\Users\magang.it8\jifas-assistant\Jifas.Assistant\KnowledgeBase"
$OllamaUrl  = "http://10.0.12.54:11434"
$EmbedModel = "qwen3-embedding:4b"
$ChunkSize  = 500   # karakter per chunk
$ChunkOverlap = 50  # overlap antar chunk
$Now        = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " JIFAS KB Reload — $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# ── HELPER: Embed via Ollama ─────────────────────────────────
function Get-Embedding([string]$text) {
    $body = @{ model = $EmbedModel; input = $text } | ConvertTo-Json -Compress
    try {
        $resp = Invoke-RestMethod -Uri "$OllamaUrl/api/embed" `
            -Method POST -ContentType "application/json" -Body $body -TimeoutSec 120
        $vec = $resp.embeddings[0]
        if ($null -eq $vec) { return $null }
        return ($vec -join ",")   # simpan sebagai CSV string
    } catch {
        Write-Warning "  [Embed] Error: $($_.Exception.Message)"
        return $null
    }
}

# ── HELPER: Chunk text ───────────────────────────────────────
function Get-Chunks([string]$text, [int]$size, [int]$overlap) {
    $chunks = @()
    $start  = 0
    $len    = $text.Length
    while ($start -lt $len) {
        $end = [Math]::Min($start + $size, $len)
        $chunk = $text.Substring($start, $end - $start).Trim()
        if ($chunk.Length -gt 10) {
            $chunks += [PSCustomObject]@{ Content = $chunk; Start = $start; End = $end }
        }
        $start = $end - $overlap
        if ($start -ge $len - 10) { break }
    }
    return $chunks
}

# ── HELPER: Derive category from folder ─────────────────────
function Get-Category([string]$folder) {
    $map = @{
        "Invoice"     = "Invoice"
        "Pum"         = "PUM"
        "Receiving"   = "Receiving"
        "Payment"     = "Payment"
        "Cashbank"    = "Cashbank"
        "Accounting"  = "Accounting"
        "Report"      = "Report"
        "Master"      = "Master"
        "OverBudget"  = "OverBudget"
        "Budget"      = "Budget"
    }
    foreach ($key in $map.Keys) {
        if ($folder -like "*\$key*" -or $folder -like "*/$key*") { return $map[$key] }
    }
    return "General"
}

# ── STEP 1: Connect DB ───────────────────────────────────────
Write-Host "`n[1/4] Connecting to database..." -ForegroundColor Yellow
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnStr)
$conn.Open()
Write-Host "      Connected OK" -ForegroundColor Green

# ── STEP 2: Truncate tables ──────────────────────────────────
Write-Host "`n[2/4] Truncating KnowledgeBaseChunks & KnowledgeBaseDocuments..." -ForegroundColor Yellow
$cmd = $conn.CreateCommand()
$cmd.CommandText = "DELETE FROM KnowledgeBaseChunks; DELETE FROM KnowledgeBaseDocuments; DBCC CHECKIDENT('KnowledgeBaseDocuments', RESEED, 1); DBCC CHECKIDENT('KnowledgeBaseChunks', RESEED, 1);"
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "      Tables cleared" -ForegroundColor Green

# ── STEP 3: Load & insert documents ─────────────────────────
Write-Host "`n[3/4] Loading KB files..." -ForegroundColor Yellow
$files = Get-ChildItem $KbRoot -Recurse -Filter "*.txt" | Sort-Object FullName
$totalFiles = $files.Count
Write-Host "      Found $totalFiles files"

$docCount   = 0
$chunkCount = 0
$errCount   = 0

foreach ($file in $files) {
    $docCount++
    $content  = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $title    = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $category = Get-Category $file.DirectoryName
    $filePath = $file.FullName

    Write-Host "  [$docCount/$totalFiles] $($file.Name) ($($content.Length) chars, cat=$category)" -NoNewline

    # Insert document
    $insertDoc = $conn.CreateCommand()
    $insertDoc.CommandText = @"
INSERT INTO KnowledgeBaseDocuments
    (Title, Content, Category, Tags, FilePath, Embedding, EmbeddingDimensions,
     IsActive, CreatedAt, UpdatedAt, ViewCount, RelevanceScore, CreatedBy, UpdatedBy)
VALUES
    (@Title, @Content, @Category, @Tags, @FilePath, NULL, 0,
     1, @Now, @Now, 0, 1.0, 'system', 'system');
SELECT SCOPE_IDENTITY();
"@
    $insertDoc.Parameters.AddWithValue("@Title",    $title)    | Out-Null
    $insertDoc.Parameters.AddWithValue("@Content",  $content)  | Out-Null
    $insertDoc.Parameters.AddWithValue("@Category", $category) | Out-Null
    $insertDoc.Parameters.AddWithValue("@Tags",     $category) | Out-Null
    $insertDoc.Parameters.AddWithValue("@FilePath", $filePath) | Out-Null
    $insertDoc.Parameters.AddWithValue("@Now",      $Now)      | Out-Null

    $docId = [int]$insertDoc.ExecuteScalar()

    # Generate doc-level embedding
    $docEmbed = Get-Embedding ($content.Substring(0, [Math]::Min(1000, $content.Length)))
    if ($docEmbed) {
        $updDoc = $conn.CreateCommand()
        $updDoc.CommandText = "UPDATE KnowledgeBaseDocuments SET Embedding=@E, EmbeddingDimensions=@D WHERE Id=@Id"
        $updDoc.Parameters.AddWithValue("@E", $docEmbed) | Out-Null
        $updDoc.Parameters.AddWithValue("@D", ($docEmbed.Split(",").Count)) | Out-Null
        $updDoc.Parameters.AddWithValue("@Id", $docId) | Out-Null
        $updDoc.ExecuteNonQuery() | Out-Null
    }

    # Chunk & embed
    $chunks = Get-Chunks $content $ChunkSize $ChunkOverlap
    $chunkIdx = 0
    foreach ($chunk in $chunks) {
        $embed = Get-Embedding $chunk.Content
        $dim   = if ($embed) { $embed.Split(",").Count } else { 0 }

        $insertChunk = $conn.CreateCommand()
        $insertChunk.CommandText = @"
INSERT INTO KnowledgeBaseChunks
    (DocumentId, ChunkIndex, Content, Embedding, EmbeddingDimensions,
     StartCharPos, EndCharPos, CreatedAt, UpdatedAt)
VALUES
    (@DocId, @Idx, @Content, @Embed, @Dim, @Start, @End, @Now, @Now)
"@
        $insertChunk.Parameters.AddWithValue("@DocId",   $docId)          | Out-Null
        $insertChunk.Parameters.AddWithValue("@Idx",     $chunkIdx)        | Out-Null
        $insertChunk.Parameters.AddWithValue("@Content", $chunk.Content)   | Out-Null
        $insertChunk.Parameters.AddWithValue("@Embed",   $(if($embed){$embed}else{[DBNull]::Value})) | Out-Null
        $insertChunk.Parameters.AddWithValue("@Dim",     $dim)             | Out-Null
        $insertChunk.Parameters.AddWithValue("@Start",   $chunk.Start)     | Out-Null
        $insertChunk.Parameters.AddWithValue("@End",     $chunk.End)       | Out-Null
        $insertChunk.Parameters.AddWithValue("@Now",     $Now)             | Out-Null
        $insertChunk.ExecuteNonQuery() | Out-Null

        $chunkIdx++
        $chunkCount++
    }

    Write-Host " -> $chunkIdx chunks" -ForegroundColor Green
}

# ── STEP 4: Done ─────────────────────────────────────────────
$conn.Close()
Write-Host "`n[4/4] DONE!" -ForegroundColor Cyan
Write-Host "      Documents inserted : $docCount" -ForegroundColor Green
Write-Host "      Total chunks       : $chunkCount" -ForegroundColor Green
Write-Host "      Errors             : $errCount" -ForegroundColor $(if($errCount -gt 0){"Red"}else{"Green"})
Write-Host "`nKnowledgeBase reload selesai $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Cyan
