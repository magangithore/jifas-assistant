# Local AI Integration Guide

## ?? Overview

JIFAS Assistant API sekarang mendukung **Local AI Server** menggunakan Ollama dengan model **Qwen3:8b** sebagai pengganti Google Gemini API.

### Benefits:
- ? **No API Key required** - Menggunakan server lokal kantor
- ? **Faster responses** - No internet latency
- ? **Privacy** - Data tidak keluar dari jaringan kantor
- ? **Cost-free** - Tidak ada biaya per API call
- ? **Easy switching** - Interface kompatibel dengan GeminiService

---

## ?? Configuration

### appsettings.json

```json
"LocalAI": {
  "BaseUrl": "http://10.0.12.54:11434",
  "Model": "qwen3:8b",
  "Temperature": 0.7,
  "TopP": 0.9,
  "TopK": 40,
  "TimeoutSeconds": 30
}
```

**Parameters:**
- `BaseUrl`: URL Ollama server (IP:Port dari kantor)
- `Model`: Model name yang digunakan (qwen3:8b)
- `Temperature`: 0-2 (lower = deterministic, higher = creative)
- `TopP`: 0-1 nucleus sampling (default 0.9 untuk balanced)
- `TopK`: Top-K sampling (default 40)
- `TimeoutSeconds`: Request timeout (default 30 detik)

---

## ?? Dependency Injection

### Program.cs - Aktif

```csharp
// ? ACTIVE - Using Local AI (Ollama/Qwen3)
builder.Services.AddScoped<IGeminiService, LocalAIService>();

// ? DISABLED - Using Gemini API (comment out if using LocalAI)
// builder.Services.AddScoped<IGeminiService, GeminiService>();
```

### Switching back ke Gemini

Jika mau kembali ke Gemini API, cukup swap 2 baris di atas:

```csharp
// ? DISABLED - Using Local AI
// builder.Services.AddScoped<IGeminiService, LocalAIService>();

// ? ACTIVE - Using Gemini API
builder.Services.AddScoped<IGeminiService, GeminiService>();
```

---

## ?? Testing Local AI Connection

### Option 1: Run Test Harness

```bash
# Create test project (atau run di existing project)
dotnet new console -n LocalAITest

# Run test
cd LocalAITest
dotnet run
```

### Option 2: Test via cURL

```bash
# Test 1: Check server availability
curl http://10.0.12.54:11434/api/tags

# Test 2: Simple prompt
curl http://10.0.12.54:11434/api/generate \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{
    "model": "qwen3:8b",
    "prompt": "Siapa yang membuat Anda?",
    "stream": false
  }'

# Test 3: Check response format
curl http://10.0.12.54:11434/api/generate \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{
    "model": "qwen3:8b",
    "prompt": "Halo!",
    "stream": false,
    "temperature": 0.7,
    "top_p": 0.9,
    "top_k": 40
  }' | jq '.response'
```

### Option 3: Use Postman/Insomnia

**POST** to: `http://10.0.12.54:11434/api/generate`

**Headers:**
```
Content-Type: application/json
```

**Body:**
```json
{
  "model": "qwen3:8b",
  "prompt": "Apa itu JIFAS?",
  "stream": false,
  "temperature": 0.7,
  "top_p": 0.9,
  "top_k": 40
}
```

**Expected Response:**
```json
{
  "model": "qwen3:8b",
  "created_at": "2024-02-21T10:30:00Z",
  "response": "JIFAS adalah Jababeka Integrated Finance Accounting System...",
  "done": true,
  "total_duration": 1234567890,
  "load_duration": 123456,
  "prompt_eval_count": 20,
  "prompt_eval_duration": 345678,
  "eval_count": 50,
  "eval_duration": 765432
}
```

---

## ?? Quality Metrics

### Response Quality

**Strengths:**
- ? Good for Indonesian language (Qwen3 trained on Chinese + multilingual)
- ? Fast inference (8B model = ~1-3 seconds per response)
- ? Contextual understanding
- ? Can follow instructions well

**Limitations:**
- ?? No real-time internet knowledge (knowledge cutoff)
- ?? Smaller model compared to Gemini (8B vs 70B+)
- ?? May hallucinate without proper KB context

### Best Practices

1. **Always provide Knowledge Base context** - Jangan hanya prompt saja
2. **Use clear prompts** - Model akan lebih akurat dengan instruksi jelas
3. **Set appropriate temperature**:
   - 0.3-0.5 = Faktual, konsisten (untuk finance data)
   - 0.7-0.9 = Balanced (default)
   - 1.0-1.5 = Creative, varied (untuk suggestions)

---

## ?? API Endpoints (Ollama Compatible)

### Generate Response
**POST** `/api/generate`

```json
{
  "model": "qwen3:8b",
  "prompt": "Your prompt here",
  "stream": false,
  "temperature": 0.7,
  "top_p": 0.9,
  "top_k": 40
}
```

### List Available Models
**GET** `/api/tags`

Response:
```json
{
  "models": [
    {
      "name": "qwen3:8b",
      "modified_at": "2024-02-21T10:00:00Z",
      "size": 4000000000,
      "digest": "sha256:xxxx..."
    }
  ]
}
```

### Pull/Download Model
**POST** `/api/pull`

```json
{
  "name": "qwen3:8b"
}
```

---

## ?? Service Architecture

### LocalAIService.cs

**Implements:** `IGeminiService` (same interface as Gemini)

**Methods:**
- `GenerateResponseAsync(query, kbResults)` - Gunakan KB + local AI
- `GenerateSuggestionsAsync(query, response)` - Generate saran lanjutan
- `IsInScopeAsync(query)` - Check apakah pertanyaan dalam scope JIFAS
- `CallLocalAIAsync(prompt)` - Direct API call
- `CallGeminiApiAsync(prompt)` - Alias untuk CallLocalAIAsync

**Features:**
- ? Automatic error handling
- ? Query normalization
- ? Suggestion extraction
- ? Scope detection
- ? Structured logging

---

## ?? Server Configuration

### Ollama Server Requirements

**Minimum:**
- CPU: 4 cores
- RAM: 8GB (16GB recommended for qwen3:8b)
- Port: 11434 (default)

**Current Setup:**
- Server: 10.0.12.54
- Port: 11434
- Model: qwen3:8b

### Check Ollama Status

```bash
# SSH ke server
ssh user@10.0.12.54

# Check if Ollama running
systemctl status ollama

# View logs
journalctl -u ollama -f

# Check model status
curl localhost:11434/api/tags
```

### If Server Down

```bash
# Restart Ollama
sudo systemctl restart ollama

# Or start manually
ollama serve

# Pull model jika belum ada
ollama pull qwen3:8b
```

---

## ?? Troubleshooting

### Issue 1: Connection Refused

```
Error: dial tcp 10.0.12.54:11434: connect: connection refused
```

**Solutions:**
1. Check jika server Ollama running: `curl http://10.0.12.54:11434/api/tags`
2. Check IP address: Pastikan 10.0.12.54 benar
3. Check firewall: Port 11434 harus terbuka
4. Check network: Pastikan bisa ping ke 10.0.12.54

### Issue 2: Model Not Found

```
Error: model 'qwen3:8b' not found
```

**Solutions:**
1. Check available models: `curl http://10.0.12.54:11434/api/tags`
2. Pull model: `curl -X POST http://10.0.12.54:11434/api/pull -d '{"name":"qwen3:8b"}'`
3. Wait for pull to complete (bisa 5-10 menit tergantung internet)

### Issue 3: Timeout

```
Error: context deadline exceeded
```

**Solutions:**
1. Increase timeout: Update `TimeoutSeconds` di appsettings.json
2. Check server load: SSH ke server, run `top`
3. Reduce concurrent requests: Limit API calls
4. Use simpler prompts: Reduce inference time

### Issue 4: Poor Response Quality

**Symptoms:** Jawaban tidak relevan, tidak sesuai, aneh

**Solutions:**
1. Pastikan KB context diberikan (jangan prompt saja)
2. Use PromptEngineeringService untuk build better prompts
3. Adjust temperature (turunkan untuk factual answers)
4. Verify prompt instructions jelas dan specific
5. Test dengan manual prompt dulu di Ollama

---

## ?? Performance Tuning

### Parameters untuk optimize

**Untuk Accuracy (Finance Data):**
```json
{
  "temperature": 0.3,
  "top_p": 0.8,
  "top_k": 30
}
```

**Untuk Speed:**
```json
{
  "temperature": 0.9,
  "top_p": 0.95,
  "top_k": 50
}
```

**Untuk Balance (Default):**
```json
{
  "temperature": 0.7,
  "top_p": 0.9,
  "top_k": 40
}
```

### Monitoring

Monitor response time:
```csharp
var startTime = DateTime.Now;
var response = await _geminiService.GenerateResponseAsync(query, results);
var duration = DateTime.Now - startTime;
_logger.LogInformation($"Response generated in {duration.TotalSeconds}s");
```

---

## ?? Fallback Strategy

Jika Local AI tidak tersedia, setup fallback:

```csharp
// Services/ChatService.cs
try 
{
    response = await _localAIService.GenerateResponseAsync(query, kbResults);
}
catch (HttpRequestException)
{
    // Fallback ke default response atau Gemini
    _logger.LogWarning("Local AI unavailable, using fallback");
    response = GetDefaultResponse(query);
}
```

---

## ?? Logging

LocalAIService menghasilkan logs seperti:

```
[LocalAIService] Initialized with model: qwen3:8b at http://10.0.12.54:11434
[LocalAIService] Processing query: apa itu jifas
[LocalAIService] Found 3 KB results (relevance: 92%)
[LocalAIService] Calling local AI with prompt
[LocalAIService] Generated response length: 245 characters
```

Check logs di: `Logs/jifas-chatbot-{Date}.log`

---

## ?? Deployment

### Development
? Using LocalAIService (development-friendly)

### Staging
? Can use LocalAIService or Gemini

### Production
- Option 1: LocalAIService (recommended untuk cost efficiency)
- Option 2: GeminiService (jika butuh higher quality)

### Switching Strategy

**Untuk switch antara Local AI dan Gemini:**

1. **Update Program.cs:**
   ```csharp
   // Change this line:
   builder.Services.AddScoped<IGeminiService, LocalAIService>();
   // To:
   builder.Services.AddScoped<IGeminiService, GeminiService>();
   ```

2. **Rebuild:**
   ```bash
   dotnet build -c Release
   ```

3. **Test:**
   ```bash
   dotnet run
   ```

4. **Deploy** sesuai environment

---

## ?? Support

**Jika ada masalah dengan Local AI:**

1. Check Ollama server status: `curl http://10.0.12.54:11434/api/tags`
2. Check logs: `ssh user@10.0.12.54` ? `journalctl -u ollama -f`
3. Check network: `ping 10.0.12.54`
4. Test manual prompt di Ollama
5. Contact: IT Server Team untuk Ollama support

---

## ?? References

- Ollama Documentation: https://github.com/ollama/ollama
- Qwen3 Model Card: https://huggingface.co/Qwen/Qwen3-8B
- API Endpoint Docs: http://10.0.12.54:11434/docs (jika available)
- JIFAS Assistant Guide: `../README_DOCUMENTATION.md`

---

**Last Updated:** 2024-02-21  
**Status:** ? Production Ready  
**Version:** 1.0
