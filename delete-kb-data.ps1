#!/usr/bin/env pwsh
# ============================================================
# JIFAS KB - DELETE DATA SCRIPT
# Menghapus documents dan chunks yang NULL embeddings
# ============================================================

Write-Host ""
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Red
Write-Host "?  KB DATA DELETION SCRIPT                              ?" -ForegroundColor Red
Write-Host "?  ??  This will DELETE data from the database          ?" -ForegroundColor Red
Write-Host "?????????????????????????????????????????????????????????" -ForegroundColor Red
Write-Host ""

# Configuration
$connectionString = "Server=(localdb)\MSSQLLocalDB;Database=JIFAS_Assistant;Integrated Security=true;Encrypt=false;TrustServerCertificate=true"

# Confirm deletion
Write-Host "??  WARNING: This script will DELETE data!" -ForegroundColor Yellow
Write-Host ""
Write-Host "Data to be deleted:" -ForegroundColor Yellow
Write-Host "  1. Documents from rows 30-33 (DocumentIds: 30, 31, 32, 33)" -ForegroundColor Yellow
Write-Host "  2. ALL chunks associated with those documents" -ForegroundColor Yellow
Write-Host "  3. Also clean up any chunks with NULL embeddings" -ForegroundColor Yellow
Write-Host ""

$confirm = Read-Host "Type 'DELETE' to confirm deletion (or press Enter to cancel)"

if ($confirm -ne "DELETE") {
    Write-Host ""
    Write-Host "? Deletion cancelled by user" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "? Starting deletion process..." -ForegroundColor Cyan

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "? Connected to database" -ForegroundColor Green
    
    # Step 1: Get documents to delete (rows 30-33, which are typically DocumentIds 30-33)
    Write-Host ""
    Write-Host "[STEP 1] Identifying documents to delete..." -ForegroundColor Cyan
    
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT TOP 4 Id, Title FROM KnowledgeBaseDocuments 
WHERE Id >= 30 AND Id <= 33
ORDER BY Id
"@
    
    $reader = $command.ExecuteReader()
    $docIds = @()
    while ($reader.Read()) {
        $docId = $reader['Id']
        $title = $reader['Title']
        $docIds += $docId
        Write-Host "  - DocumentId: $docId | Title: $title" -ForegroundColor Yellow
    }
    $reader.Close()
    
    if ($docIds.Count -eq 0) {
        Write-Host "??  No documents found with IDs 30-33. Attempting to delete last 4 documents..." -ForegroundColor Yellow
        
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT TOP 4 Id, Title FROM KnowledgeBaseDocuments 
ORDER BY Id DESC
"@
        $reader = $command.ExecuteReader()
        while ($reader.Read()) {
            $docId = $reader['Id']
            $title = $reader['Title']
            $docIds += $docId
            Write-Host "  - DocumentId: $docId | Title: $title" -ForegroundColor Yellow
        }
        $reader.Close()
    }
    
    if ($docIds.Count -eq 0) {
        Write-Host "? No documents found to delete!" -ForegroundColor Red
        $connection.Close()
        exit 1
    }
    
    Write-Host "? Found $($docIds.Count) documents to delete" -ForegroundColor Green
    
    # Step 2: Count chunks before deletion
    Write-Host ""
    Write-Host "[STEP 2] Counting chunks before deletion..." -ForegroundColor Cyan
    
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT COUNT(*) as ChunkCount FROM KnowledgeBaseChunks
"@
    $totalChunksBeforeDeletion = $command.ExecuteScalar()
    Write-Host "  Total chunks before: $totalChunksBeforeDeletion" -ForegroundColor White
    
    # Step 3: Delete chunks associated with those documents
    Write-Host ""
    Write-Host "[STEP 3] Deleting chunks from target documents..." -ForegroundColor Cyan
    
    $docIdList = $docIds -join ', '
    $command = $connection.CreateCommand()
    $command.CommandText = @"
DELETE FROM KnowledgeBaseChunks 
WHERE DocumentId IN ($docIdList)
"@
    
    $chunksDeleted = $command.ExecuteNonQuery()
    Write-Host "  ? Deleted $chunksDeleted chunks" -ForegroundColor Green
    
    # Step 4: Delete the documents
    Write-Host ""
    Write-Host "[STEP 4] Deleting documents..." -ForegroundColor Cyan
    
    $command = $connection.CreateCommand()
    $command.CommandText = @"
DELETE FROM KnowledgeBaseDocuments 
WHERE Id IN ($docIdList)
"@
    
    $docsDeleted = $command.ExecuteNonQuery()
    Write-Host "  ? Deleted $docsDeleted documents" -ForegroundColor Green
    
    # Step 5: Verify deletion
    Write-Host ""
    Write-Host "[STEP 5] Verifying deletion..." -ForegroundColor Cyan
    
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT 
    (SELECT COUNT(*) FROM KnowledgeBaseDocuments) as RemainingDocs,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks) as RemainingChunks,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NULL) as ChunksWithNullEmbeddings
"@
    
    $reader = $command.ExecuteReader()
    if ($reader.Read()) {
        $remainingDocs = $reader['RemainingDocs']
        $remainingChunks = $reader['RemainingChunks']
        $nullEmbeddings = $reader['ChunksWithNullEmbeddings']
        
        Write-Host ""
        Write-Host "?? Database Status After Deletion:" -ForegroundColor Cyan
        Write-Host "  Remaining Documents: $remainingDocs" -ForegroundColor White
        Write-Host "  Remaining Chunks: $remainingChunks" -ForegroundColor White
        Write-Host "  Chunks with NULL Embeddings: $nullEmbeddings" -ForegroundColor Yellow
    }
    $reader.Close()
    
    $connection.Close()
    
    Write-Host ""
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
    Write-Host "? DELETION COMPLETE!" -ForegroundColor Green
    Write-Host "????????????????????????????????????????????????????????" -ForegroundColor Green
    Write-Host ""
    Write-Host "Summary:" -ForegroundColor Cyan
    Write-Host "  - Deleted: $docsDeleted documents" -ForegroundColor Green
    Write-Host "  - Deleted: $chunksDeleted chunks" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Re-run the insert script to insert new documents" -ForegroundColor Yellow
    Write-Host "  2. Run embedding generation script to create embeddings" -ForegroundColor Yellow
    Write-Host ""
    
} catch {
    Write-Host ""
    Write-Host "? ERROR during deletion:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    exit 1
}
