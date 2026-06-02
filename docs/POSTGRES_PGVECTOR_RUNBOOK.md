# PostgreSQL pgvector Runbook

## Start Database

```powershell
docker compose up -d jifas-postgres
docker compose ps jifas-postgres
```

The compose service uses `pgvector/pgvector:pg16` and creates:

- Database: `jifas_assistant`
- User: `jifas`
- Default local password: `jifas_dev_password`
- Port: `5432`

Override these through `.env` before starting Docker.

## Run API Locally

```powershell
dotnet run --project Jifas.Assistant
```

On startup the API ensures the `vector` extension exists, creates the schema when needed, and adds the `EmbeddingVector` column.

The current embedding model returns 2560-dimensional vectors. pgvector can store and search these vectors, but HNSW indexes are limited to 2000 dimensions for `vector`, so this project uses exact pgvector cosine search for now.

## Reindex Knowledge Base

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ReindexKnowledgeBase.ps1
```

The reindex script calls `KBLoader` and writes both:

- `Embedding`: JSON fallback embedding storage
- `EmbeddingVector`: pgvector column used by PostgreSQL cosine search

## Smoke Checks

```powershell
Invoke-RestMethod http://localhost:5000/health
Invoke-RestMethod http://localhost:5000/api/chat/health
Invoke-RestMethod http://localhost:5000/api/KnowledgeBaseSearch/health
```

## Golden Evaluation

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-GoldenEvaluation.ps1 -BaseUrl http://localhost:5000
```

The output JSON is written to `golden-evaluation-results.json` by default. Treat this as a local test artifact and avoid committing result files unless a report is explicitly needed.
