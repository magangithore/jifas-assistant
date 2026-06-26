-- ============================================================
-- Migration: AddLastSessionIdColumn
-- Track the last session ID per user for cross-session memory
-- PostgreSQL-compatible
-- ============================================================

DO $$
BEGIN
    -- Add LastSessionId if not exists
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'UserMemory' AND column_name = 'LastSessionId'
    ) THEN
        ALTER TABLE "UserMemory" ADD COLUMN "LastSessionId" varchar(100);
        RAISE NOTICE 'Column LastSessionId added to UserMemory.';
    ELSE
        RAISE NOTICE 'Column LastSessionId already exists — skipped.';
    END IF;

    -- Add LastSessionAt if not exists
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'UserMemory' AND column_name = 'LastSessionAt'
    ) THEN
        ALTER TABLE "UserMemory" ADD COLUMN "LastSessionAt" timestamp with time zone;
        RAISE NOTICE 'Column LastSessionAt added to UserMemory.';
    ELSE
        RAISE NOTICE 'Column LastSessionAt already exists — skipped.';
    END IF;
END $$;
