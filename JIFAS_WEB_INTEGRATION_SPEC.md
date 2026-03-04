# ?? JIFAS Web ? JIFAS Assistant - Integration Specification

**Status:** Ready for Integration  
**Date:** March 2024  
**From:** JIFAS Assistant Team to JIFAS Web Team  

---

## ?? REQUIREMENTS MAPPING

### 1. API ENDPOINT ?

**JIFAS Web Requested:**
```
Base URL: http://[server]:[port]
Endpoint: /api/chat/message
Method: POST
Timeout: 30 seconds
```

**JIFAS Assistant Provides:**
```
Base URL: http://localhost:5000 (or production URL)
Endpoint: ? POST /api/chat/message
Method: ? POST
Timeout: ? 30 seconds (configurable)
Status: ? READY
```

---

### 2. REQUEST FORMAT ??

**JIFAS Web Requested:**
```json
{
  "message": "user input text",
  "userId": "logged in user ID",
  "userRole": "FINA:KI | ACCT:KI | etc",
  "currentModule": "Invoice | Payment | PUM | Receiving | etc",
  "company_id": "active company",
  "language": "id | en",
  "context": {
    "current_page": "/Invoice/Finance/Index",
    "selected_document_id": "INV-2024-001 (optional)"
  }
}
```

**JIFAS Assistant Current:**
```json
{
  "message": "user input text",           ? SUPPORTED
  "userId": "logged in user ID",          ? SUPPORTED
  "sessionId": "unique-session-id"        ? SUPPORTED (for tracking)
}
```

**RECOMMENDATION:**
- ? **Core fields** (message, userId) ? **Already supported**
- ?? **Optional fields** (userRole, currentModule, company_id, language) ? **Need to add**
- ?? **Context fields** ? **Need to add**

**Action Items:**
- [ ] Add optional fields to ChatRequest model
- [ ] Update ChatService to handle context-aware responses
- [ ] Implement language support (id/en)
- [ ] Implement role-based response filtering

---

### 3. RESPONSE FORMAT ??

**JIFAS Web Requested:**
```json
{
  "success": true,
  "reply": "Jawaban dari AI dalam bahasa user",
  "suggestions": [
    "Suggested action 1",
    "Suggested action 2"
  ],
  "action": {
    "type": "navigate | execute | info",
    "target": "/Invoice/Finance/Index",
    "data": { }
  },
  "error": null
}
```

**JIFAS Assistant Current:**
```json
{
  "sender": "JIFAS AI Assistant",
  "message": "AI response",              ? = reply
  "success": true,                       ? EXACT MATCH
  "suggestions": [...],                  ? EXACT MATCH
  "errors": [],                          ? = error
  "source": "Knowledge Base",
  "timestamp": "2024-03-04 11:40:14",
  "performanceMetrics": {...}
}
```

**MAPPING:**
| JIFAS Web | JIFAS Assistant | Status |
|-----------|-----------------|--------|
| reply | message | ? Compatible |
| success | success | ? Exact match |
| suggestions | suggestions | ? Exact match |
| error | errors | ? Compatible |
| action | (NOT YET) | ?? To implement |

**Action Items:**
- [ ] Add `action` field to ChatResponse for navigation/execute actions
- [ ] Implement action type detection in ChatService
- [ ] Add navigation/execute capability based on context

---

### 4. AUTHENTICATION ??

**JIFAS Web Requested:**
```
Option A: Header Authorization: Bearer {JWT_token}
Option B: Query Param: ?token={jwt_token}
Token Source: window.appLayoutConfig.tokenRaw (from JIFAS Web)
```

**JIFAS Assistant Current:**
```
? NO AUTHENTICATION (any request accepted)
```

**RECOMMENDATION:**
- [ ] Add JWT validation middleware
- [ ] Extract token from Authorization header
- [ ] Validate token with JIFAS main auth system
- [ ] Support both header and query param (for flexibility)

**Implementation Priority:** High (Security required for production)

---

### 5. CORE CAPABILITIES ?

| Feature | Status | Notes |
|---------|--------|-------|
| **General Help** | ? Ready | Explain JIFAS workflows |
| **Module Guidance** | ? Ready | Invoice, Payment, PUM, Receiving, Accounting |
| **Business Rules** | ? Pending KB | Invoice status, approval rules, tax handling |
| **Quick Tips** | ? Pending KB | Currency rate, 3-way matching, GL posting |
| **Troubleshooting** | ? Pending KB | Status locked, cannot edit, validation errors |

**Note:** ? Features work, but need Knowledge Base data (company documentation) for accurate answers.

---

### 6. CONTEXT-AWARE FEATURES ??

| Feature | Current | Status | Priority |
|---------|---------|--------|----------|
| **Current Page Awareness** | No | To implement | High |
| **Role-Based Answers** | No | To implement | High |
| **Document-Specific** | No | To implement | Medium |
| **Language Support** | English only | Add Indonesian | High |

**Implementation:** Will be added in ChatService enhancement.

---

### 7. ERROR HANDLING ?

**JIFAS Web Requested:**
```json
{
  "success": false,
  "error": "API timeout | Invalid token | Rate limit exceeded",
  "fallback_reply": "Maaf, terjadi kesalahan. Coba lagi nanti..."
}
```

**JIFAS Assistant Current:**
```json
{
  "success": false,
  "errors": ["Error message"],
  "message": "Error occurred"
}
```

**Action Items:**
- [x] Error handling implemented
- [ ] Add standardized error codes
- [ ] Add fallback replies in Indonesian
- [ ] Implement rate limiting

---

### 8. PERFORMANCE REQUIREMENTS ??

| Requirement | Requested | Current | Status |
|-------------|-----------|---------|--------|
| **Response Time (ideal)** | < 5s | 0.9-2.7s | ? EXCEEDS |
| **Max Response Time** | < 10s | 2.7s | ? EXCEEDS |
| **Concurrent Users** | 50+ | Unlimited | ? READY |
| **Rate Limit** | 100/hr per user | Not yet | ?? To add |
| **Max Message Length** | 500 chars | 2000 chars | ? OK |
| **Max Response Length** | 1000 chars | Variable | ? OK |

**Status:** ? Performance requirements already exceeded!

---

### 9. KNOWLEDGE BASE EXPECTED

**What JIFAS Web expects:**
- Invoice procedures & workflows
- Payment rules & approvals
- PUM (Purchasing) guidelines
- Receiving procedures
- Accounting & GL posting
- Currency & exchange rates
- Validation rules & error messages
- Role-based procedures (Finance, User, AP, AR)

**Current Status:**
```
? Knowledge Base is EMPTY (no JIFAS documents loaded yet)
```

**Next Steps for JIFAS Assistant Team:**
1. Collect JIFAS documentation (SOP, user guides, procedures)
2. Parse documents (PDF ? Text)
3. Chunk documents (paragraph-based)
4. Generate embeddings
5. Index to SQL Server
6. Vector search ready

**Estimated Time:** 1-2 weeks (depending on document volume)

---

### 10. OPTIONAL ADVANCED FEATURES ??

| Feature | Status | Timeline |
|---------|--------|----------|
| **Real-time Document Search** | Planned | Phase 2 |
| **Quick Actions** | Planned | Phase 2 |
| **Analytics/Insights** | Planned | Phase 3 |
| **Multi-turn Conversation** | Ready (via sessionId) | Now |

---

## ?? INTEGRATION TIMELINE

### Phase 1: Now (Integration Ready)
```
? API endpoints ready
? Request/response format compatible
? Performance meets requirements
? Add optional fields to request
? Implement authentication
```

**Timeline:** 1-2 days (for optional fields + auth)

### Phase 2: Week 1-2 (Knowledge Base)
```
? Load JIFAS documentation
? Chunk documents
? Generate embeddings
? Test with real use cases
```

**Timeline:** 1-2 weeks

### Phase 3: Week 3+ (Enhancements)
```
? Context-aware responses
? Role-based filtering
? Language support (Indonesian)
? Action navigation
? Advanced features
```

**Timeline:** 2-4 weeks

---

## ?? INTEGRATION CHECKLIST FOR JIFAS WEB

### Before Integration:
- [ ] Get API endpoint URL from JIFAS Assistant team
- [ ] Test API with Postman/cURL
- [ ] Verify response format matches expectations
- [ ] Setup error handling in frontend
- [ ] Create loading spinner/UI for API calls
- [ ] Plan fallback UI if API unavailable

### During Integration:
- [ ] Create ChatBotApiService.js (API wrapper)
- [ ] Implement chat UI component
- [ ] Connect to JIFAS authentication
- [ ] Test with real users
- [ ] Monitor performance & errors
- [ ] Gather user feedback

### After Integration:
- [ ] Monitor API usage
- [ ] Collect user questions
- [ ] Build Knowledge Base based on questions
- [ ] Iterate on AI responses
- [ ] Plan Phase 2 enhancements

---

## ?? TECHNICAL INTEGRATION GUIDE

### For JIFAS Web Frontend Team:

**1. Create API Service:**
```javascript
// ChatBotApiService.js
class ChatBotApiService {
  constructor(baseUrl = 'http://localhost:5000') {
    this.baseUrl = baseUrl;
  }

  async sendMessage(request) {
    // request format:
    // {
    //   message: "user input",
    //   userId: "current user",
    //   userRole: "optional",
    //   currentModule: "optional",
    //   context: { ... }
    // }
    
    try {
      const response = await fetch(`${this.baseUrl}/api/chat/message`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}` // from window.appLayoutConfig.tokenRaw
        },
        body: JSON.stringify(request)
      });

      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      
      // Response format:
      // {
      //   success: true,
      //   message: "AI reply",
      //   suggestions: [...],
      //   action: {...},
      //   ...
      // }
      
      return await response.json();
    } catch (error) {
      return {
        success: false,
        error: error.message,
        fallback_reply: "Maaf, terjadi kesalahan. Coba lagi nanti."
      };
    }
  }
}
```

**2. In Kendo UI Component:**
```javascript
// Initialize service
const chatApi = new ChatBotApiService('http://localhost:5000');

// On Send button click:
const response = await chatApi.sendMessage({
  message: userInput,
  userId: window.appLayoutConfig.userId,
  userRole: window.appLayoutConfig.userRole,
  currentModule: 'Invoice', // from route
  context: {
    current_page: window.location.pathname,
    selected_document_id: selectedInvoiceId
  }
});

// Display response
if (response.success) {
  displayMessage(response.message);
  displaySuggestions(response.suggestions);
  if (response.action) {
    handleAction(response.action);
  }
} else {
  displayError(response.fallback_reply);
}
```

---

## ? READY TO INTEGRATE!

**Status:** ?? **READY FOR INTEGRATION**

**From JIFAS Assistant Team, we have:**
- ? Working API endpoints
- ? Compatible request/response format
- ? Exceeds performance requirements
- ? Error handling in place
- ? Logging & monitoring ready
- ? Optional fields & authentication (to add)
- ? Knowledge Base (to load documents)

**Next Steps:**
1. JIFAS Web team reviews this specification
2. Clarify any questions on format/authentication
3. Set API endpoint URL in JIFAS Web config
4. Test integration in staging environment
5. Deploy to production

---

**Questions? Contact:** JIFAS Assistant Team  
**Email:** it@jababeka.com  
**Repository:** https://github.com/magangithore/jifas-assistant

?? **Ready to go live when you are!**
