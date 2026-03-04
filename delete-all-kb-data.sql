-- ============================================================
-- SCRIPT MENGHAPUS SEMUA DATA DI TABLE KNOWLEDGEBASE
-- ============================================================

-- OPTION 1: Hapus semua chunks dan documents
-- ============================================================

-- Hapus semua chunks
DELETE FROM KnowledgeBaseChunks;

-- Hapus semua documents
DELETE FROM KnowledgeBaseDocuments;

-- Verify
SELECT 
    'KnowledgeBaseDocuments' as TableName,
    COUNT(*) as RemainingRows
FROM KnowledgeBaseDocuments
UNION ALL
SELECT 
    'KnowledgeBaseChunks' as TableName,
    COUNT(*) as RemainingRows
FROM KnowledgeBaseChunks;

-- Reset identity (auto-increment)
DBCC CHECKIDENT ('KnowledgeBaseDocuments', RESEED, 0);
DBCC CHECKIDENT ('KnowledgeBaseChunks', RESEED, 0);

PRINT 'All knowledge base data has been deleted!';
PRINT 'Identity values reset to 0.';
