# Git Commit Sequence

After setup is complete, commit changes in this order:

## Step 1: Configure
```bash
git add Jifas.Assistant/appsettings.json
git commit -m "config: Update database name to JIFAS_Assistant and remove Qdrant"
```

## Step 2: Add Database Script
```bash
git add JIFAS_Assistant_Database.sql
git commit -m "db: Add fresh database creation script"
```

## Step 3: Update Seeding Service
```bash
# Option A: Replace old one
git rm Jifas.Assistant/Services/KBSeedingService.cs
git add Jifas.Assistant/Services/KBSeedingService_Simplified.cs
git mv Jifas.Assistant/Services/KBSeedingService_Simplified.cs Jifas.Assistant/Services/KBSeedingService.cs
git commit -m "refactor: Simplify KB seeding - remove Qdrant, use SQL Server only"

# Option B: Keep both (add new)
git add Jifas.Assistant/Services/KBSeedingService_Simplified.cs
git commit -m "feat: Add simplified KB seeding service (SQL Server only)"
```

## Step 4: Add Documentation
```bash
git add 00_START_HERE.md FINAL_SETUP_GUIDE.md KB_SETUP_GUIDE.md CLEANUP_GUIDE.md
git commit -m "docs: Add comprehensive setup guides and cleanup instructions"
```

## Step 5: Cleanup (Optional - do later)
```bash
# After verifying everything works, delete old services:
git rm Jifas.Assistant/Services/QdrantVectorService.cs
git rm Jifas.Assistant/Services/QdrantSeedingService.cs
git rm Jifas.Assistant/Services/QdrantInitializer.cs
git rm Jifas.Assistant/Services/IQdrantVectorService.cs
git rm Jifas.Assistant/Services/IQdrantInitializer.cs
git rm Jifas.Assistant/Services/KnowledgeBaseEmbeddingService.cs
git rm Jifas.Assistant/Services/IKnowledgeBaseEmbeddingService.cs
git rm Jifas.Assistant/Services/ConversationService.cs
git rm Jifas.Assistant/Services/CommonQueryCacheService.cs

git commit -m "refactor: Remove unused Qdrant and duplicate embedding services"
```

## Step 6: Update Program.cs (if cleaned up)
```bash
git add Jifas.Assistant/Program.cs
git commit -m "refactor: Remove Qdrant service registrations from DI container"
```

## Full Log (After All Steps)
```bash
git log --oneline -n 10
# Should show:
# xyz1234 refactor: Remove Qdrant service registrations from DI container
# xyz1233 refactor: Remove unused Qdrant and duplicate embedding services
# xyz1232 docs: Add comprehensive setup guides and cleanup instructions
# xyz1231 refactor: Simplify KB seeding - remove Qdrant, use SQL Server only
# xyz1230 db: Add fresh database creation script
# xyz1229 config: Update database name to JIFAS_Assistant and remove Qdrant
```

---

## View Changes Before Committing

```bash
# See what changed
git status

# See exact changes
git diff Jifas.Assistant/appsettings.json
git diff Jifas.Assistant/Program.cs

# Verify files to delete
git rm --dry-run <filename>  # Preview without deleting
```

---

## Push to Remote

```bash
git push origin master
# or
git push origin master -f  # Only if needed (force)
```

---

## Verify Commits

```bash
# Check last commit
git log -1

# Check specific file history
git log --oneline Jifas.Assistant/Services/KBSeedingService.cs

# Check changes in last commit
git show HEAD

# Push changes
git push
```
