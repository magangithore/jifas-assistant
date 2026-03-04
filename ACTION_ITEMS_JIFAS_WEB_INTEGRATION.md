# ? ACTION ITEMS - JIFAS Assistant Enhancement untuk JIFAS Web Integration

**Priority:** High  
**Timeline:** 1-2 weeks  
**Owner:** JIFAS Assistant Team  

---

## ?? Phase 1: Immediate (1-2 days)

### 1. Enhance ChatRequest Model ?
**File:** `Jifas.Assistant/models/ChatRequest.cs`

**Add Optional Fields:**
```csharp
public string UserRole { get; set; }           // e.g., "FINA:KI", "ACCT:KI"
public string CurrentModule { get; set; }      // e.g., "Invoice", "Payment", "PUM"
public string CompanyId { get; set; }          // Active company
public string Language { get; set; } = "id";   // Default: Indonesian
public ContextInfo Context { get; set; }       // Nested context

public class ContextInfo
{
    public string CurrentPage { get; set; }    // e.g., "/Invoice/Finance/Index"
    public string SelectedDocumentId { get; set; } // e.g., "INV-2024-001"
}
```

**Effort:** 30 minutes  
**Status:** ?? READY TO IMPLEMENT

---

### 2. Enhance ChatResponse Model ?
**File:** `Jifas.Assistant/models/ChatResponse.cs`

**Add Action Field:**
```csharp
public ActionInfo Action { get; set; }

public class ActionInfo
{
    public string Type { get; set; }     // "navigate" | "execute" | "info"
    public string Target { get; set; }   // e.g., "/Invoice/Finance/Index"
    public Dictionary<string, object> Data { get; set; }
}
```

**Effort:** 30 minutes  
**Status:** ?? READY TO IMPLEMENT

---

### 3. Add JWT Authentication Middleware ?
**New File:** `Jifas.Assistant/Middleware/AuthenticationMiddleware.cs`

**Features:**
- Extract token from Authorization header (`Bearer {token}`)
- Support query parameter fallback (`?token={token}`)
- Validate token with JIFAS main auth system
- Add user context to request pipeline

**Effort:** 2-3 hours  
**Status:** ?? READY TO IMPLEMENT

**Template:**
```csharp
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuthenticationService _authService;

    public AuthenticationMiddleware(RequestDelegate next, IAuthenticationService authService)
    {
        _next = next;
        _authService = authService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract token from header or query param
        var token = ExtractToken(context);
        
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                // Validate token with JIFAS auth system
                var user = await _authService.ValidateTokenAsync(token);
                context.Items["CurrentUser"] = user;
            }
            catch
            {
                context.Response.StatusCode = 401;
                return;
            }
        }

        await _next(context);
    }

    private string ExtractToken(HttpContext context)
    {
        // Try header first
        if (context.Request.Headers.TryGetValue("Authorization", out var auth))
        {
            var parts = auth.ToString().Split(' ');
            if (parts.Length == 2 && parts[0] == "Bearer")
                return parts[1];
        }

        // Try query param
        context.Request.Query.TryGetValue("token", out var queryToken);
        return queryToken.ToString();
    }
}
```

---

### 4. Implement Language Support (Indonesian/English) ?
**File:** `Jifas.Assistant/Services/ChatService.cs`

**Add Localization:**
```csharp
private string GetLocalizedMessage(string messageKey, string language)
{
    var messages = language == "id" ? IndonesianMessages : EnglishMessages;
    return messages.ContainsKey(messageKey) ? messages[messageKey] : messageKey;
}
```

**Effort:** 1-2 hours  
**Status:** ?? READY TO IMPLEMENT

---

### 5. Update ChatService for Context-Awareness ?
**File:** `Jifas.Assistant/Services/ChatService.cs`

**Enhancements:**
- Accept context from request
- Adjust prompt based on currentModule
- Filter answers by userRole
- Add action suggestions based on context

**Effort:** 3-4 hours  
**Status:** ?? READY TO IMPLEMENT

---

## ?? Phase 2: Knowledge Base Setup (1-2 weeks)

### 6. Document Collection ??
**Action:**
- [ ] Collect all JIFAS SOP documentation
- [ ] Collect user guides (Invoice, Payment, PUM, Receiving, Accounting)
- [ ] Collect business rule documents
- [ ] Collect role-based procedure docs

**Format:** PDF, Word, or plain text  
**Effort:** Depends on doc availability  
**Owner:** JIFAS Business team

---

### 7. Document Parsing Service ?
**File:** `Jifas.Assistant/Services/DocumentParsingService.cs`

**Support:**
- PDF extraction (iTextSharp)
- Word extraction (DocumentFormat.OpenXml)
- Plain text reading

**Effort:** 2-3 hours  
**Status:** ?? READY TO IMPLEMENT

---

### 8. Text Chunking Service ?
**File:** `Jifas.Assistant/Services/TextChunkingService.cs`

**Implementation:** Paragraph-based (recommended from CHUNKING_STRATEGY.md)

```csharp
public List<string> ChunkByParagraph(string text, int minLength = 50)
{
    var chunks = text.Split(new[] { "\r\n\r\n", "\n\n" }, 
        StringSplitOptions.RemoveEmptyEntries);
    
    return chunks
        .Where(c => c.Length > minLength)
        .ToList();
}
```

**Effort:** 1 hour  
**Status:** ?? READY TO IMPLEMENT

---

### 9. Knowledge Base Ingestion Pipeline ?
**File:** `Jifas.Assistant/Services/KnowledgeBaseIngestionService.cs`

**Flow:**
1. Upload document ? Parse ? Chunk ? Generate embeddings ? Store in DB

**Effort:** 2-3 hours  
**Status:** ?? READY TO IMPLEMENT

---

### 10. Embedding Generation via Ollama ?
**File:** `Jifas.Assistant/Services/EmbeddingService.cs`

**Use:** Ollama's embedding model for vector generation

**Effort:** 1-2 hours  
**Status:** ?? READY TO IMPLEMENT

---

## ?? Summary of Tasks

| # | Task | Effort | Timeline | Priority |
|---|------|--------|----------|----------|
| 1 | Enhance ChatRequest | 30m | Now | High |
| 2 | Enhance ChatResponse | 30m | Now | High |
| 3 | Add JWT Authentication | 2-3h | Now | High |
| 4 | Language Support (id/en) | 1-2h | Now | Medium |
| 5 | Context-Aware Responses | 3-4h | Now | High |
| **SUBTOTAL** | **Phase 1** | **~8-10h** | **1-2 days** | |
| 6 | Document Collection | Varies | Week 1 | High |
| 7 | Document Parsing | 2-3h | Week 1 | High |
| 8 | Text Chunking | 1h | Week 1 | High |
| 9 | KB Ingestion Pipeline | 2-3h | Week 1 | High |
| 10 | Embedding Service | 1-2h | Week 1 | High |
| **SUBTOTAL** | **Phase 2** | **~9-12h + doc time** | **1-2 weeks** | |

---

## ?? Implementation Roadmap

### Week 1 (Now - 1-2 days)
```
[ ] Task 1: Enhance ChatRequest
[ ] Task 2: Enhance ChatResponse
[ ] Task 3: Add JWT Authentication
[ ] Task 4: Language Support
[ ] Task 5: Context-Aware Service
[ ] COMPLETE: Deploy Phase 1 to staging
[ ] TEST: Integration test with JIFAS Web team
```

### Week 2-3 (Knowledge Base)
```
[ ] Task 6: Collect JIFAS documents
[ ] Task 7: Implement Document Parsing
[ ] Task 8: Implement Text Chunking
[ ] Task 9: Build Ingestion Pipeline
[ ] Task 10: Embedding generation
[ ] COMPLETE: Load KB data
[ ] TEST: Real use case testing
```

### Week 4+ (Enhancements)
```
[ ] Role-based answer filtering
[ ] Document-specific context
[ ] Advanced features (actions, navigation)
[ ] User analytics & monitoring
[ ] Performance optimization
```

---

## ?? BLOCKERS & DEPENDENCIES

| Item | Status | Notes |
|------|--------|-------|
| JWT Token from JIFAS Auth | ? Pending | Need validation endpoint |
| JIFAS Documents | ? Pending | Need SOP collection |
| JIFAS Module Details | ? Pending | For context-aware responses |
| Ollama Embedding Model | ? Ready | gemma3:4b can generate embeddings |

---

## ?? Team Assignments

| Role | Task | Timeline |
|------|------|----------|
| **Backend Dev (C#)** | Tasks 1-5, 7-10 | 8-12h Week 1 + 9-12h Week 2-3 |
| **Business Analyst** | Task 6 (collect docs) | Week 1-2 |
| **QA/Testing** | Integration testing | Week 1 end, Week 2-3 |

---

## ? ACCEPTANCE CRITERIA

### Phase 1 Complete When:
- [ ] All optional fields added to ChatRequest
- [ ] All response fields added to ChatResponse
- [ ] JWT authentication working
- [ ] Language parameter respected in responses
- [ ] Context affects response content
- [ ] All changes deployed & tested

### Phase 2 Complete When:
- [ ] All JIFAS documents loaded
- [ ] Documents chunked successfully
- [ ] Embeddings generated & indexed
- [ ] KB search returns relevant results
- [ ] AI answers based on KB data
- [ ] Performance metrics acceptable

---

## ?? QUESTIONS & CLARIFICATIONS

**Need from JIFAS Web Team:**
1. JWT token endpoint/format for validation?
2. Expected response time SLA?
3. Rate limiting requirements?
4. Preferred language (id/en)?
5. Priority modules to support first?

**Need from JIFAS Business:**
1. Document priority order?
2. Which roles have different requirements?
3. Key workflows to document?
4. Existing knowledge base systems?

---

**Status:** Ready to start Phase 1 immediately! ??
**Questions?** Contact: it@jababeka.com
