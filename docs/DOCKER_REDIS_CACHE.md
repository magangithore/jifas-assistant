# Docker Redis Cache Runbook

## Tujuan

Redis dipakai sebagai distributed cache untuk jawaban chatbot dan cache Knowledge Base. Redis mengurangi beban Ollama dan PostgreSQL saat pertanyaan berulang. Suggestion AI terpisah tidak lagi dicache karena pipeline suggestion LLM sudah dimatikan.

## Komponen

- Service: `jifas-redis`
- Image: `redis:7-alpine`
- Port lokal: `6379`
- Persistence: `appendonly yes`
- Data volume: `redis_data`

## Konfigurasi

```powershell
ConnectionStrings__Redis=jifas-redis:6379
Caching__UseRedis=true
Caching__RedisInstanceName=JIFAS:
Caching__EnableResponseCache=true
Caching__ResponseCacheDurationHours=24
Caching__EnableKBCache=true
```

## Strategi Cache

- `shared`: dipakai untuk pertanyaan umum seperti `Apa itu JIFAS?`, penjelasan modul, alur umum, dan navigasi umum yang tidak mengandung konteks user/dokumen.
- `contextual`: dipakai untuk pertanyaan yang membawa konteks user, role, company, halaman aktif, dokumen, status, tiket, atau issue personal.
- Ticket flow, request invalid, out-of-scope, dan error response tidak masuk response cache.
- Jika Redis tidak tersedia, aplikasi fallback ke memory cache/no-cache tanpa mematikan request chat utama.

## Operasi

Cek health:

```powershell
docker exec jifas-redis redis-cli ping
docker exec jifas-redis redis-cli DBSIZE
```

Lihat key sample:

```powershell
docker exec jifas-redis redis-cli --scan --pattern "JIFAS:*" | Select-Object -First 20
```

Flush cache lokal:

```powershell
docker exec jifas-redis redis-cli FLUSHDB
```

## Catatan Production

- Cache adalah optimasi; API harus tetap bisa jalan walau Redis sementara error.
- Jangan simpan secret di cache key.
- Untuk load test 50 VU, Redis hit rate harus membantu request berulang dan menurunkan tekanan ke Ollama.
- `scripts\Run-ChatStressTest.ps1` melakukan warmup cache secara default sebelum 50 VU paralel; gunakan `-SkipWarmup` hanya untuk cold-start exploratory test.
- Field `performanceMetrics.cacheScope` dan `performanceMetrics.wasCacheLit` dipakai untuk membaca cache behavior di test/report.
