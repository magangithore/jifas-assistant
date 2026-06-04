# AI Quality Runbook

## Tujuan

Runbook ini dipakai untuk memvalidasi kualitas jawaban chatbot JIFAS secara repeatable sebelum rilis.

## Golden Evaluation

Jalankan evaluasi Knowledge Base:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-GoldenEvaluation.ps1 -BaseUrl http://localhost:8888
```

Output default: `golden-evaluation-results.json`.

## Functional Smoke

Minimal scenario sebelum rilis:

- `Apa itu JIFAS?`
- pertanyaan halaman aktif, contoh Invoice approval;
- out-of-scope, contoh cuaca/film;
- ticket flow cancel path;
- health monitoring dashboard.
- ulangi pertanyaan umum yang sama dari user berbeda untuk memastikan shared response cache berjalan.

Runner otomatis:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1 -BaseUrl http://localhost:8888
```

Untuk validasi Jira end-to-end yang membuat tiket asli:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1 -BaseUrl http://localhost:8888 -CreateRealJiraTicket
```

Ticket real Jira hanya dibuat jika switch `-CreateRealJiraTicket` dipakai. Tiket test memakai prefix `[TEST]` dan boleh ditutup setelah diverifikasi.

## Stress Baseline

Gunakan 50 virtual users:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Acceptance:

- tidak ada HTTP 5xx;
- container API/Postgres/Redis tetap healthy;
- HTTP 429 harus 0 selama rate limit chat sedang dinonaktifkan;
- report JSON dan Markdown tersimpan.
- warmup shared cache berhasil sebelum 50 VU paralel, kecuali `-SkipWarmup` dipakai untuk exploratory cold test.
- monitoring error tidak boleh didominasi `suggestions`, karena suggestion LLM terpisah sudah tidak dipakai.
- `suggestionsTotalMs` pada report stress normalnya 0 ms untuk chat utama.
- jika Jira real test diminta, report wajib berisi ticket key Jira asli, bukan `OFFLINE-*`.

## Kriteria Kualitas Jawaban

- Jawaban harus tentang JIFAS.
- Tidak menyebut detail internal seperti `Knowledge Base` ke user.
- Jika data tidak tersedia, arahkan ke IT/Finance/Accounting/Tax sesuai masalah.
- Untuk masalah teknis, minta nomor dokumen, company, status, screenshot, dan waktu kejadian.
- Arahan lanjutan harus berada di dalam isi jawaban utama, bukan mengandalkan field `suggestions`.
- Field `suggestions` hanya dipertahankan untuk kompatibilitas frontend lama dan boleh kosong.
- Service suggestion terpisah tidak diregistrasikan lagi di runtime supaya tidak ada call LLM kedua.
