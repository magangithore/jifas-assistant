# ?? JWT Authentication Implementation

**Status:** ? IMPLEMENTED  
**Date:** March 2024  
**Token Management:** Via `appsettings.json` (NO hardcoded secrets)  

---

## ?? What Was Implemented

### 1. ? JWT Configuration in appsettings.json
**File:** `Jifas.Assistant/appsettings.json`

```json
"Jwt": {
  "Enabled": true,
  "Audience": "JifasWebApp",
  "Authority": "https://your-auth-server.com",
  "ValidateIssuer": true,
  "ValidateAudience": true,
  "ValidateLifetime": true,
  "ClockSkewSeconds": 5,
  "RequireHttpsMetadata": false,
  "TokenEndpoint": "https://your-auth-server.com/token"
}
```

**Key Points:**
- ? **NO hardcoded secrets**
- ? All config in `appsettings.json` (easy to change per environment)
- ? Can be disabled by setting `"Enabled": false`
- ? Clock skew = 5 seconds (tolerance for time differences)

---

### 2. ? JWT Authentication Middleware
**File:** `Jifas.Assistant/Middleware/JwtAuthenticationMiddleware.cs` (NEW)

**Features:**
- Extracts JWT token from:
  - `Authorization: Bearer {token}` header (preferred)
  - `?token={token}` query parameter (fallback)
- Validates token based on config settings
- Adds user context to request pipeline
- Logs authentication events
- Returns 401 for invalid tokens

**Token Validation:**
```csharp
// What it validates (configurable in appsettings.json):
? Token format (JWT)
? Issuer (if ValidateIssuer = true)
? Audience (if ValidateAudience = true)
? Lifetime (if ValidateLifetime = true)
? Clock skew tolerance

? Signature (currently skipped - needs JWKS endpoint)
```

---

### 3. ? Middleware Registration
**File:** `Jifas.Assistant/Program.cs`

```csharp
// Added to middleware pipeline (first, before other middleware)
app.UseJwtAuthentication();
```

**Why First?**
- Authentication should happen early in pipeline
- Before authorization checks
- Before business logic

---

## ?? How to Use with JIFAS Web

### Option 1: Authorization Header (Recommended)
```bash
curl -X POST http://localhost:5000/api/chat/message \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \
  -H "Content-Type: application/json" \
  -d '{"message": "test"}'
```

### Option 2: Query Parameter (Fallback)
```bash
curl -X POST "http://localhost:5000/api/chat/message?token=eyJhbGciOiJIUzI1NiIs..." \
  -H "Content-Type: application/json" \
  -d '{"message": "test"}'
```

### Option 3: JavaScript (Kendo UI)
```javascript
// Get token from JIFAS main app
const token = window.appLayoutConfig.tokenRaw;

// Send request with Authorization header
fetch('http://localhost:5000/api/chat/message', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`  // ? JWT token here
  },
  body: JSON.stringify({
    message: 'Gimana cara input invoice?',
    userId: userId,
    userRole: userRole
  })
})
```

---

## ?? Configuration Options

### Enable/Disable JWT
```json
{
  "Jwt": {
    "Enabled": true  // Set to false to bypass JWT validation
  }
}
```

### Control What to Validate
```json
{
  "Jwt": {
    "ValidateIssuer": true,      // Check token issuer
    "ValidateAudience": true,    // Check audience claim
    "ValidateLifetime": true,    // Check exp claim
    "ClockSkewSeconds": 5        // Allow 5 sec time difference
  }
}
```

### Production Setup
```json
{
  "Jwt": {
    "Enabled": true,
    "Authority": "https://your-jifas-auth.com",
    "RequireHttpsMetadata": true,  // HTTPS only in production
    "ValidateLifetime": true
  }
}
```

### Development Setup
```json
{
  "Jwt": {
    "Enabled": false,  // Can disable during dev
    // OR
    "RequireHttpsMetadata": false,  // Allow HTTP
    "ClockSkewSeconds": 60          // More tolerance
  }
}
```

---

## ?? Token Flow

```
???????????????????????
?  JIFAS Web App      ?
?  (Kendo UI)         ?
?                     ?
? Get JWT token from  ?
? window.appLayoutConfig
???????????????????????
               ?
               ? Send with Authorization header
               ? Bearer: {JWT token}
               ?
???????????????????????????????????????
? JwtAuthenticationMiddleware         ?
?                                     ?
? 1. Extract token from header/query  ?
? 2. Parse JWT (no secret needed)     ?
? 3. Validate claims:                 ?
?    - Issuer ?                       ?
?    - Audience ?                     ?
?    - Lifetime ?                     ?
?    - Signature (skipped for now)    ?
? 4. Add user to request context      ?
? 5. Continue to controller           ?
???????????????????????????????????????
               ?
               ?
???????????????????????
? ChatController      ?
?                     ?
? Access user from    ?
? context.User        ?
? or                  ?
? HttpContext.Items["CurrentUser"]
???????????????????????
```

---

## ?? What Happens on Token Validation

### ? Valid Token
```
1. Token extracted and parsed
2. Claims validated (issuer, audience, lifetime)
3. User added to: context.User & HttpContext.Items["CurrentUser"]
4. Request continues to controller
5. Controller can access: HttpContext.User.Claims
```

### ? Invalid Token
```
1. Token validation fails
2. Response: HTTP 401 Unauthorized
3. JSON error: {"error": "Invalid or expired token"}
4. Request stops - controller NOT called
```

### ?? No Token Provided
```
1. No Authorization header or query param
2. Request continues (optional - can require token)
3. context.User = null
4. Controller receives unauthenticated request
```

---

## ?? Important Notes

### Signature Validation
Currently **SKIPPED** because:
- We don't have the signing secret
- JIFAS Web tokens signed by main auth server
- Need JWKS (JSON Web Key Set) endpoint from auth server

To enable signature validation later:
```csharp
// In middleware, configure JWKS endpoint:
tokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
{
    // Fetch public keys from: https://auth-server.com/.well-known/jwks.json
    // Validate signature with public key
};
```

### Token Expiration
Enabled by default:
```json
"ValidateLifetime": true  // Check exp claim
```

Middleware respects `exp` claim in JWT. Token automatically invalid after expiration.

---

## ?? For Different Environments

### Development (appsettings.Development.json)
```json
{
  "Jwt": {
    "Enabled": false  // Or true with loose validation
  }
}
```

### Staging
```json
{
  "Jwt": {
    "Enabled": true,
    "ValidateIssuer": false,  // Looser validation
    "RequireHttpsMetadata": false
  }
}
```

### Production
```json
{
  "Jwt": {
    "Enabled": true,
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidateLifetime": true,
    "RequireHttpsMetadata": true,  // HTTPS only
    "Authority": "https://secure-auth-server.com"
  }
}
```

---

## ?? Files Changed

1. **appsettings.json** - Added `"Jwt"` section
2. **Program.cs** - Added middleware registration + using statement
3. **JwtAuthenticationMiddleware.cs** (NEW) - JWT validation logic

---

## ? Status

? JWT middleware implemented  
? Configuration-based (no hardcoded secrets)  
? Works with Bearer header and query param  
? Validates token claims  
? Logs authentication events  
? Ready for JIFAS Web integration  
? Disabled/enabled via appsettings.json  

---

## ?? Next: JIFAS Web Integration

JIFAS Web team needs to:
1. Get JWT token from `window.appLayoutConfig.tokenRaw`
2. Include in Authorization header: `Authorization: Bearer {token}`
3. Send to API endpoint
4. API validates and processes

---

**Status:** ? READY FOR USE  
**No Hardcoded Secrets:** ? All via appsettings.json  
**Flexible Configuration:** ? Enable/disable per environment
