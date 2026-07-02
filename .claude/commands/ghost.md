# Ghost — Hidden behavior & dead code analysis

## Usage
```
/ghost [optional: specific file or area to investigate]
```

## Prompt Template

Kamu adalah code ghost hunter. Carilah hal-hal yang MENGHILANG dari visibility:

## Cakupan Analisis

### 1. Dead Code (Unused Code Paths)
- Methods/classes yang DECLARED tapi TIDAK DIPANGGIL dari manapun
- #ifdef / conditional compilation blocks yang tidak pernah dieksekusi
- Edge cases yang kode ADA tapi condition tidak pernah true
- Fallback paths yang tidak pernah triggered
- Service registrations (DI) untuk services yang tidak pernah di-resolve

### 2. Hidden Behavior
- Side effects yang tidak obvious (mutating input, writing to disk, sending
  network requests as byproduct)
- Silent failures (catch blocks yang swallow exception tanpa logging)
- Default values yang behavior-nya tidak dokumentasi
- Cache behavior yang tidak expected (TTL, eviction, serialization format)
- Async/await pitfalls (fire-and-forget, unobserved task exceptions)

### 3. Ghost Dependencies
- Startup code yang berjalan sebelum kamu melihat trace
- Static constructors / module initializers
- Middleware registration order yang punya side effect
- Background services / hosted services yang berjalan tanpa di-request
- Health checks yang punya side effect

### 4. Silent Data Flows
- Data yang dimodify IN-PLACE vs. returned as new object
- Collection mutations yang tidak obvious
- String operations yang allocate besar tanpa kamu sadari
- Object lifetime vs. DI scope mismatches

### 5. Configuration Ghosts
- Env vars / config yang dibaca tapi tidak ada validation
- Secrets yang assumed ada tanpa fail-safe
- Default values yang active di production tanpa kamu sadari
- Feature flags yang nggak konsisten

## Task

Analisa scope:
$ARGUMENTS

Untuk setiap ghost yang ditemukan:
1. **Type**: dead-code / hidden-behavior / ghost-dependency / silent-data / config-ghost
2. **Location**: file + line numbers
3. **What it does**: jelaskan apa yang sebenarnya terjadi
4. **Why it's dangerous**: impact jika ini吃到 (reached)
5. **How to detect**: bagaimana kamu menemukan ini (tool, teknik, reasoning)

Export sebagai categorized list.
