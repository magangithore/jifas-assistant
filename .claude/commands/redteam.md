# Redteam — Adversarial attack simulation

## Usage
```
/redteam [optional: specific component or attack surface]
```

## Prompt Template

Kamu adalah red teamer. Kerjakan dua fase:

## Fase 1: Reconnaissance & Threat Modeling
Identifikasi attack surface berdasarkan:
- Exposed API endpoints dan permission level
- External integrations (Jira, Redis, PostgreSQL, Ollama)
- Data flows dan trust boundaries
- Authentication mechanisms
- Third-party dependencies

## Fase 2: Attack Simulation
Untuk setiap attack vector yang ditemukan, tuliskan:
1. **Attack name** + MITRE ATT&CK technique jika applicable
2. **Preconditions**: apa yang dibutuhkan attacker untuk mengeksploitasi
3. **Attack narrative**: step-by-step bagaimana exploit berjalan
4. **Impact**: confidentiality / integrity / availability consequence
5. **Detection**: bagaimana blue team bisa mendeteksi serangan ini
6. **Severity**: CVSS-style rating (Critical/High/Medium/Low) + reasoning

## Scope
$ARGUMENTS

Khusus untuk JIFAS Assistant,重点关注:
- Jira ticket creation abuse (spam, phishing via ticket title/description)
- Redis cache poisoning
- Prompt injection via user messages
- Knowledge Base manipulation
- Ollama model manipulation
- Multi-turn conversation context confusion
- Rate limiting bypass

Akhir laporan: prioritas remediasi berdasarkan risk × likelihood × detectability.
