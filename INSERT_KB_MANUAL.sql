-- ============================================
-- JIFAS Knowledge Base Manual Insert Script
-- Insert 27 KB files langsung ke database
-- ============================================

-- IMPORTANT: 
-- 1. Copy content dari setiap .txt file di knowledge-base folder
-- 2. Replace [CONTENT_HERE] dengan actual content
-- 3. Embeddings bisa diisi kemudian (for now bisa null atau dummy)

-- ============================================
-- MASTER DATA FILES (13 files)
-- ============================================

-- 1. Budget.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Budget',
    '[COPY CONTENT FROM Master/Budget.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata,budget',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 2. Company.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Company',
    '[COPY CONTENT FROM Master/Company.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata,company',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 3. AccPeriod.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'AccPeriod',
    '[COPY CONTENT FROM Master/AccPeriod.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata,accounting',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 4. COA.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'COA',
    '[COPY CONTENT FROM Master/COA.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 5. Department.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Department',
    '[COPY CONTENT FROM Master/Department.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 6. Division.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Division',
    '[COPY CONTENT FROM Master/Division.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 7. Employee.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Employee',
    '[COPY CONTENT FROM Master/Employee.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 8. General.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'General',
    '[COPY CONTENT FROM Master/General.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 9. ListCoa.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'ListCoa',
    '[COPY CONTENT FROM Master/ListCoa.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 10. ReportSetup.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'ReportSetup',
    '[COPY CONTENT FROM Master/ReportSetup.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 11. Roles.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Roles',
    '[COPY CONTENT FROM Master/Roles.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 12. Vendor.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Vendor',
    '[COPY CONTENT FROM Master/Vendor.txt HERE]',
    'Master Data',
    'jifas,kb,masterdata',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- INVOICE FILES (5 files)
-- ============================================

-- 13. ApprovallIncomplete.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'ApprovallIncomplete',
    '[COPY CONTENT FROM Invoice/ApprovallIncomplete.txt HERE]',
    'Invoice',
    'jifas,kb,invoice,approval',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 14. Create.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Create',
    '[COPY CONTENT FROM Invoice/Create.txt HERE]',
    'Invoice',
    'jifas,kb,invoice',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 15. Finance.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Finance',
    '[COPY CONTENT FROM Invoice/Finance.txt HERE]',
    'Invoice',
    'jifas,kb,invoice',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 16. HeadApproval.txt (Invoice)
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'HeadApproval',
    '[COPY CONTENT FROM Invoice/HeadApproval.txt HERE]',
    'Invoice',
    'jifas,kb,invoice,approval',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 17. Tax.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Tax',
    '[COPY CONTENT FROM Invoice/Tax.txt HERE]',
    'Invoice',
    'jifas,kb,invoice',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- PUM FILES (4 files)
-- ============================================

-- 18. HeadApproval.txt (PUM)
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'HeadApproval_PUM',
    '[COPY CONTENT FROM PUM/HeadApproval.txt HERE]',
    'PUM',
    'jifas,kb,pum,approval',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 19. Pengajuan.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Pengajuan',
    '[COPY CONTENT FROM PUM/Pengajuan.txt HERE]',
    'PUM',
    'jifas,kb,pum',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 20. PPUM&Realization.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'PPUM&Realization',
    '[COPY CONTENT FROM PUM/PPUM&Realization.txt HERE]',
    'PUM',
    'jifas,kb,pum',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 21. TaxApproval.txt (PUM)
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'TaxApproval_PUM',
    '[COPY CONTENT FROM PUM/TaxApproval.txt HERE]',
    'PUM',
    'jifas,kb,pum',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- PAYMENT FILES (3 files)
-- ============================================

-- 22. ListBg.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'ListBg',
    '[COPY CONTENT FROM Payment/ListBg.txt HERE]',
    'Payment',
    'jifas,kb,payment',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 23. PaymentInvoice.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'PaymentInvoice',
    '[COPY CONTENT FROM Payment/PaymentInvoice.txt HERE]',
    'Payment',
    'jifas,kb,payment,invoice',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 24. PaymentPUM.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'PaymentPUM',
    '[COPY CONTENT FROM Payment/PaymentPUM.txt HERE]',
    'Payment',
    'jifas,kb,payment,pum',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- OVERBUDGET FILES (2 files)
-- ============================================

-- 25. FinanceApproval.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'FinanceApproval',
    '[COPY CONTENT FROM OverBudget/FinanceApproval.txt HERE]',
    'Over Budget',
    'jifas,kb,overbudget,approval',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 26. HeadApproval.txt (OverBudget)
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'HeadApproval_OverBudget',
    '[COPY CONTENT FROM OverBudget/HeadApproval.txt HERE]',
    'Over Budget',
    'jifas,kb,overbudget,approval',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- GENERAL/ROOT FILES (3 files)
-- ============================================

-- 27. Budget-Status-Reference.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Budget-Status-Reference',
    '[COPY CONTENT FROM Budget-Status-Reference.txt HERE]',
    'General',
    'jifas,kb,budget,reference',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 28. Jifas.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'Jifas',
    '[COPY CONTENT FROM Jifas.txt HERE]',
    'General',
    'jifas,kb',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- 29. userguide.txt
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, Embedding, EmbeddingDimensions, IsActive, CreatedAt, CreatedBy)
VALUES (
    'userguide',
    '[COPY CONTENT FROM userguide.txt HERE]',
    'General',
    'jifas,kb,guide',
    '[]',
    3072,
    1,
    GETUTCDATE(),
    'System'
);

-- ============================================
-- VERIFY INSERT
-- ============================================

SELECT COUNT(*) as TotalDocuments FROM KnowledgeBaseDocuments;

SELECT Category, COUNT(*) as Count
FROM KnowledgeBaseDocuments
GROUP BY Category
ORDER BY Count DESC;

-- ============================================
-- NOTES:
-- - Embedding diisi dengan '[]' (empty array) untuk sekarang
-- - Bisa di-update kemudian dengan actual Gemini embeddings
-- - Chunks akan auto-created saat app seeding atau manual via SQL
-- ============================================
