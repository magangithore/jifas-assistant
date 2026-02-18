-- ============================================================
-- JIFAS KB - DELETE DATA SCRIPT (COMPREHENSIVE)
-- Hapus:
-- 1. Documents ID 31, 32, 33, 34
-- 2. Semua chunks dari documents tersebut
-- 3. Semua chunks dengan NULL embeddings (orphaned chunks)
-- ============================================================

-- STEP 1: PREVIEW - Statistik sebelum deletion
-- ============================================================
PRINT '??????????????????????????????????????????????????????????';
PRINT '?  JIFAS KB - DATA DELETION SCRIPT                      ?';
PRINT '?  Target: Documents 31-34 + NULL Embeddings            ?';
PRINT '??????????????????????????????????????????????????????????';
PRINT '';
PRINT '[STEP 1] STATISTIK SEBELUM DELETION';
PRINT '???????????????????????????????????';

SELECT 
    (SELECT COUNT(*) FROM KnowledgeBaseDocuments) as TotalDocuments,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks) as TotalChunks,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NULL) as ChunksWithNullEmbeddings,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NOT NULL) as ChunksWithEmbeddings;

PRINT '';
PRINT '[STEP 1.1] Documents yang akan dihapus:';
SELECT 
    Id,
    Title,
    Category,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE DocumentId = KnowledgeBaseDocuments.Id) as ChunkCount
FROM KnowledgeBaseDocuments 
WHERE Id IN (31, 32, 33, 34)
ORDER BY Id;

PRINT '';
PRINT '[STEP 1.2] Summary chunks dari documents 31-34:';
SELECT 
    COUNT(*) as TotalChunksToDelete,
    SUM(CASE WHEN Embedding IS NULL THEN 1 ELSE 0 END) as NullEmbeddings,
    SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) as WithEmbeddings
FROM KnowledgeBaseChunks
WHERE DocumentId IN (31, 32, 33, 34);

PRINT '';
PRINT '???????????????????????????????????';
PRINT '';

-- ============================================================
-- STEP 2: DELETE CHUNKS dari documents 31, 32, 33, 34
-- ============================================================
PRINT '[STEP 2] DELETING CHUNKS FROM DOCUMENTS 31-34';
PRINT '????????????????????????????????????????????????';

DECLARE @ChunksDeletedFromDocs INT;

DELETE FROM KnowledgeBaseChunks
WHERE DocumentId IN (31, 32, 33, 34);

SET @ChunksDeletedFromDocs = @@ROWCOUNT;
PRINT 'Deleted: ' + CAST(@ChunksDeletedFromDocs AS VARCHAR) + ' chunks from documents 31-34';

PRINT '';

-- ============================================================
-- STEP 3: DELETE CHUNKS dengan NULL EMBEDDINGS (sisa)
-- ============================================================
PRINT '[STEP 3] DELETING CHUNKS WITH NULL EMBEDDINGS';
PRINT '????????????????????????????????????????????????';

DECLARE @ChunksDeletedNullEmbeddings INT;

DELETE FROM KnowledgeBaseChunks
WHERE Embedding IS NULL OR Embedding = '';

SET @ChunksDeletedNullEmbeddings = @@ROWCOUNT;
PRINT 'Deleted: ' + CAST(@ChunksDeletedNullEmbeddings AS VARCHAR) + ' chunks with NULL embeddings';

PRINT '';

-- ============================================================
-- STEP 4: DELETE DOCUMENTS 31, 32, 33, 34
-- ============================================================
PRINT '[STEP 4] DELETING DOCUMENTS 31-34';
PRINT '????????????????????????????????????????????????';

DECLARE @DocsDeleted INT;

DELETE FROM KnowledgeBaseDocuments
WHERE Id IN (31, 32, 33, 34);

SET @DocsDeleted = @@ROWCOUNT;
PRINT 'Deleted: ' + CAST(@DocsDeleted AS VARCHAR) + ' documents';

PRINT '';
PRINT '';

-- ============================================================
-- STEP 5: VERIFY DELETION - STATISTIK AKHIR
-- ============================================================
PRINT '[STEP 5] STATISTIK SETELAH DELETION';
PRINT '???????????????????????????????????';

SELECT 
    (SELECT COUNT(*) FROM KnowledgeBaseDocuments) as RemainingDocuments,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks) as RemainingChunks,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NULL) as ChunksWithNullEmbeddings,
    (SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NOT NULL) as ChunksWithEmbeddings;

PRINT '';
PRINT '???????????????????????????????????';
PRINT '[SUMMARY] DELETION COMPLETED';
PRINT '???????????????????????????????????';
PRINT 'Documents deleted: ' + CAST(@DocsDeleted AS VARCHAR);
PRINT 'Chunks deleted from docs 31-34: ' + CAST(@ChunksDeletedFromDocs AS VARCHAR);
PRINT 'Chunks with NULL embeddings deleted: ' + CAST(@ChunksDeletedNullEmbeddings AS VARCHAR);
PRINT 'Total chunks deleted: ' + CAST((@ChunksDeletedFromDocs + @ChunksDeletedNullEmbeddings) AS VARCHAR);
PRINT '';
