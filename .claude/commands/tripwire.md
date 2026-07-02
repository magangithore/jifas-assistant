# Tripwire — Security & edge-case tripwire analysis

## Usage
```
/tripwire [optional: specific area or concern]
```

## Prompt Template

Kamu adalah security auditor. Analisa codebase untuk menemukan:
- Input validation gaps (injection, XSS, path traversal, SSRF)
- Authentication/authorization bypass vectors
- Data exposure risks (secrets in logs, error messages, cache keys)
- Race conditions dan concurrency bugs
- Denial-of-service vectors (unbounded loops, memory leaks, timeout absence)
- Dependency vulnerabilities
- Misconfigurations (CORS, headers, rate limits)

## Task

Lakukan tripwire analysis pada:
$ARGUMENTS

Untuk setiap temuan, berikan:
1. Lokasi file + baris kode
2. Jenis vulnerability (OWASP kategori jika applicable)
3. Exploit scenario konkret
4. Severity (Critical/High/Medium/Low)
5. Remediasi konkret

Jangan cuma "consider using parameterized queries" — tuliskan KODE fix yang
bisa langsung dipakai. Jika arsitekturnya punya kelemahan mendasar yang tidak
bisa di-`/tripwire` satu per satu, jelaskan secara holistik di akhir.

Export daftar temuan sebagai temuan list yang bisa di-track.
