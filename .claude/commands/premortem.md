# Premortem — Pre-failure analysis

## Usage
```
/premortem [optional: scenario or project description]
```

## Prompt Template

Kamu adalah futurist sekaligus post-mortem analyst. Kerjakan premortem analysis:

## Instruksi
Bayangkan: sistem JIFAS Assistant telah GAGAL total di production.
User tidak bisa bertanya, data bocor, Jira ticket berantakan, audit trail
hilang, dan CTO meminta penjelasan.

## Task

Untuk setiap failure scenario di bawah, berikan ROOT CAUSE ANALYSIS lengkap:

1. **Failure Scenario** (beri nama spesifik, bukan generik)

2. **What happened** — narasi 2-3 kalimat: apa yang terjadi, kapan, bagaimana
   dampaknya terasa ke user

3. **Root Cause** — penyebab teknis paling dalam:
   - Apakah ini bug kode? Arsitektur yang salah? Operational failure?
   - Apakah kegagalan ini cascading atau single point?
   - Code smells / architectural debt mana yang berkontribusi?

4. **Contributing Factors** — faktor pelengkap:
   - Apa yang TIM developer ketahui tapi tidak di-address?
   - Apa yang monitoring/alerts tidak tangkap?
   - Apa yang test coverage tidak cover?

5. **Lessons Learned** — 2-3 actionable items (bukan platitud "should have
   tested more")

6. **Preventive Controls** — apa yang SEHARUSNYA ada di code/design/process
   untuk mencegah kegagalan ini

## Prioritas Failure Scenarios
Analisa scenarios ini (dan tambah sendiri jika ada yang lebih masuk akal untuk JIFAS):
- Chatbot memberikan jawaban salah/misleading ke user finance
- Semua response kosong atau 500 error
- Jira ticket dibuat GANDAbisa tapi tidak bisa ditrack
- Knowledge Base corrupted / results contaminated
- Sensitive data (API key, user info) bocor ke response
- Redis/Postgres/Ollama outage menyebabkan cascade failure
- Prompt injection: attacker menyisipkan instruksi via chat message
- Cache poisoning: attacker mengacaukan cached responses
- Conversation history bocor antar user/session

## Scope
$ARGUMENTS

Export sebagai ranked list: likelihood × severity × preventability.
