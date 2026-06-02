# AI Quality Runbook

## User-Facing Response Policy

The chat response should stay simple for users. Internal source and retrieval details are used for confidence and monitoring, but the UI does not need to display source citations.

## Feedback API

```http
POST /api/feedback
Content-Type: application/json

{
  "chatId": null,
  "sessionId": "local-session",
  "messageId": "optional-client-message-id",
  "userId": "local-user",
  "rating": 5,
  "comment": "Jawaban membantu"
}
```

Rating must be between 1 and 5.

## Quality Monitoring

```powershell
Invoke-RestMethod "http://localhost:5000/api/monitoring/quality?minutes=60&slowThresholdMs=30000"
```

The endpoint summarizes:

- KB hit rate
- fallback response rate
- low-confidence response rate
- average confidence
- average response time
- slow responses

## Golden Questions

Golden questions live in `Jifas.Assistant/Quality/golden-questions.json`.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-GoldenEvaluation.ps1 -BaseUrl http://localhost:5000
```

Use this after:

- changing the LLM model
- changing the embedding model
- reindexing the KB
- modifying prompt or search logic
