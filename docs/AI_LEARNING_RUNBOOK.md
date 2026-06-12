# AI Learning Runbook

Dokumen ini menjelaskan flow AI Learning JIFAS Assistant. Versi pertama ini tidak melakukan fine-tuning model. Learning dilakukan dengan cara membuat knowledge baru yang sudah dikurasi admin, lalu disimpan ke Knowledge Base dan pgvector.

## Tujuan

- Menangkap pertanyaan dan jawaban chatbot yang layak dijadikan FAQ internal.
- Memastikan jawaban buruk tidak hilang, tetapi masuk antrean edit agar bisa diperbaiki.
- Mencegah data sensitif otomatis masuk Knowledge Base resmi.
- Membuat admin cukup review kandidat penting, bukan memantau semua chat satu per satu.

## Flow Data

1. User bertanya lewat `POST /api/chat/message`.
2. Chat disimpan ke `ChatHistory`.
3. User dapat memberi feedback lewat `POST /api/feedback`.
4. `AiLearningService` mengevaluasi chat berdasarkan:
   - confidence,
   - sumber Knowledge Base,
   - kualitas jawaban,
   - frekuensi pertanyaan,
   - feedback user,
   - flag sensitif,
   - kemungkinan false out-of-scope.
5. Jika layak, sistem membuat atau memperbarui `LearningCandidates`.
6. Admin membuka `/admin/learning/index.html`, lalu mengedit jawaban final.
7. Admin menandai kandidat sebagai `ReadyForPublish`.
8. Scheduler atau admin manual publish membuat `KnowledgeBaseDocuments`, chunk, embedding, dan pgvector.
9. Candidate berubah menjadi `Published`.

## Status Candidate

- `NeedsEdit`: jawaban perlu diperiksa atau diedit admin.
- `ReadyForPublish`: admin sudah menyetujui versi final.
- `Published`: sudah masuk Knowledge Base resmi.
- `Archived`: disimpan untuk audit, tidak dipublish.
- `PublishFailed`: gagal publish, biasanya karena DB atau embedding bermasalah.

Tidak ada hard delete dan tidak ada reject permanen. Jawaban salah lebih baik diedit atau diarsipkan supaya jejaknya tetap ada.

## Aturan Aman

Candidate tidak otomatis dipublish jika:

- status belum `ReadyForPublish`,
- masih ada flag sensitif,
- jawaban terlalu pendek,
- pertanyaan terlalu pendek,
- request adalah ticket flow,
- response error,
- invalid input,
- greeting atau gratitude,
- out-of-scope yang jelas bukan JIFAS.

Candidate sensitif tetap boleh masuk queue audit, tetapi wajib dibersihkan admin sebelum bisa `ReadyForPublish`.

## Admin Panel

URL:

```text
http://localhost:8888/admin/learning/index.html
```

Header wajib untuk API:

```text
X-Admin-Api-Key: isi_dari_Admin__ApiKey
```

Aksi yang tersedia:

- `Simpan Edit`
- `Tandai Siap Publish`
- `Arsipkan`
- `Collect Manual`
- `Publish Manual`

## API

```text
GET  /api/learning/stats
GET  /api/learning/candidates
GET  /api/learning/candidates/{id}
PUT  /api/learning/candidates/{id}/edit
POST /api/learning/candidates/{id}/ready
POST /api/learning/candidates/{id}/archive
POST /api/learning/collect/run
POST /api/learning/publish/run
```

Semua endpoint dilindungi policy `KnowledgeBaseAdmin`.

## Scheduler

Konfigurasi:

```json
{
  "AiLearning": {
    "Enabled": true,
    "CollectorIntervalMinutes": 10,
    "PublisherIntervalMinutes": 15,
    "MaxPublishPerRun": 10
  }
}
```

Collector memilih kandidat dari chat 30 hari terakhir. Publisher hanya memproses candidate `ReadyForPublish`.

## Monitoring

Dashboard `/monitoring/index.html` memiliki section `Learning Health`:

- pending edit,
- ready publish,
- published today,
- publish failed.

Metrik learning dipisahkan dari metrik performa AI agar dashboard tetap mudah dibaca.

## Test Cepat

```powershell
dotnet build --no-restore
dotnet test --no-restore
powershell -ExecutionPolicy Bypass -File scripts\Test-ProductionReadiness.ps1
```

Smoke manual:

1. Tanya `Apa itu JIFAS?`.
2. Kirim feedback bagus atau buruk ke chat tersebut.
3. Buka `/admin/learning/index.html`.
4. Jalankan `Collect Manual`.
5. Edit candidate.
6. Tandai `ReadyForPublish`.
7. Jalankan `Publish Manual`.
8. Tanya ulang topik yang sama dan pastikan jawaban bisa mengambil Knowledge Base baru.
