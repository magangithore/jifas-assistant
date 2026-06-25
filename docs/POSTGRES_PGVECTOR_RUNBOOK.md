# PostgreSQL pgvector Runbook

## Tujuan

JIFAS Assistant memakai PostgreSQL `pgvector` sebagai database utama dan semantic-search store. Container yang dipakai adalah `pgvector/pgvector:pg16` melalui `docker-compose.yml`.

## Komponen

- Service: `jifas-postgres`
- Database default: `jifas_assistant`
- User default lokal: `jifas`
- Port lokal: `5432`
- Extension wajib: `vector`
- Data volume: `postgres_data`

## Konfigurasi

Secret dan connection string production tidak disimpan di repo. Gunakan `.env` lokal atau secret manager.

```powershell
POSTGRES_PASSWORD=<strong-password>
ConnectionStrings__DefaultConnection=Host=jifas-postgres;Port=5432;Database=jifas_assistant;Username=jifas;Password=<strong-password>
```

## Operasi Harian

Start stack:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1
```

Cek health:

```powershell
docker ps --filter name=jifas
docker exec jifas-postgres pg_isready -U jifas -d jifas_assistant
```

Cek extension:

```powershell
docker exec jifas-postgres psql -U jifas -d jifas_assistant -c "SELECT extname FROM pg_extension WHERE extname = 'vector';"
```

## Startup dan Migration

Aplikasi menjalankan bootstrap PostgreSQL/pgvector dari script resmi:

- `Jifas.Assistant/Database/Initialize-PostgresPgvector.sql`

Script ini ikut `dotnet publish` dan dieksekusi saat startup Docker. Isinya idempotent:

- `CREATE EXTENSION IF NOT EXISTS vector;`
- tabel runtime utama jika database baru masih kosong;
- kolom `EmbeddingVector vector(2560)`;
- foreign key dan index penting untuk KB, chat history, monitoring, dan user memory.

Setelah script bootstrap, aplikasi tetap mencoba `Database.Migrate()` jika migration EF tersedia.

Jangan menghapus volume `postgres_data` kecuali sedang reset environment lokal.

## Backup Lokal

```powershell
docker exec jifas-postgres pg_dump -U jifas -d jifas_assistant > backup-jifas-assistant.sql
```

## Troubleshooting

- API gagal start: cek `ConnectionStrings__DefaultConnection`.
- Semantic search lambat: cek jumlah chunk dan apakah embedding sudah terisi.
- Extension vector tidak ada: jalankan ulang stack dan cek log API.
- Database corrupt/reset lokal: stop stack, backup dulu, baru hapus volume jika memang perlu.
