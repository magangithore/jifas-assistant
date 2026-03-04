# ?? Knowledge Base Chunking Strategy

**Untuk JIFAS Assistant - Implementasi Chunking Dokumen**

---

## ?? Chunking Strategies Tersedia

### Option 1: Simple Paragraph Chunking (RECOMMENDED) ?
**Pros:**
- Simple & mudah implementasi
- Tetap preserve semantic meaning
- Cocok untuk SOP/documentation

**Cons:**
- Paragraph size tidak uniform

**Implementation:**
```csharp
// Split by paragraphs (empty lines)
var chunks = text.Split(new[] {"\r\n\r\n", "\n\n"}, StringSplitOptions.RemoveEmptyEntries);

// Filter chunks too small
var filteredChunks = chunks.Where(c => c.Length > 50).ToList();
```

---

### Option 2: Fixed Size Chunking
**Pros:**
- Uniform chunk size
- Predictable processing

**Cons:**
- Bisa potong sentence ditengah

**Implementation:**
```csharp
// Split every 512 tokens (~2000 characters)
int chunkSize = 2000;
for (int i = 0; i < text.Length; i += chunkSize)
{
    var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
}
```

---

### Option 3: Semantic Chunking (ADVANCED)
**Pros:**
- Preserve complete sentences
- Better semantic coherence

**Cons:**
- Lebih kompleks implementasi

**Implementation:**
```csharp
// Split by sentence + combine until threshold
var sentences = text.Split(new[] {".", "!", "?"}, StringSplitOptions.RemoveEmptyEntries);
var chunks = new List<string>();
var current = "";

foreach (var sentence in sentences)
{
    if ((current + sentence).Length > 2000)
    {
        chunks.Add(current);
        current = sentence;
    }
    else
    {
        current += " " + sentence;
    }
}
if (!string.IsNullOrEmpty(current)) chunks.Add(current);
```

---

### Option 4: Sliding Window Chunking
**Pros:**
- Overlap = better context preservation

**Cons:**
- Lebih many chunks = lebih slow

**Implementation:**
```csharp
int chunkSize = 2000;
int overlap = 200;
var chunks = new List<string>();

for (int i = 0; i < text.Length - chunkSize; i += (chunkSize - overlap))
{
    chunks.Add(text.Substring(i, chunkSize));
}
```

---

## ?? Recommended Flow for JIFAS

### Step 1: Upload Document
```
User upload file ? Server receive ? Store raw file
```

### Step 2: Parse Document
```
- If PDF: Extract text from PDF
- If Word: Extract text from .docx
- If TXT: Read directly
```

### Step 3: Chunk Document (RECOMMENDED: Option 1 - Paragraph)
```
Input: Full document text
?
Split by paragraphs
?
Filter small chunks (<50 char)
?
Output: List of chunks
```

### Step 4: Generate Embeddings
```
For each chunk:
  - Send to Ollama embedding model
  - Get vector representation
  - Store vector + metadata to DB
```

### Step 5: Index to SQL Server
```
INSERT INTO KnowledgeBaseChunks (
  DocumentId,
  ChunkNumber,
  Content,
  Vector,
  CreatedAt
)
```

### Step 6: Query & Retrieve
```
User query
?
Generate embedding untuk query
?
Find similar chunks via vector search
?
Return top K results
```

---

## ?? Implementation (Para Diadopsi Later)

### Service: ITextChunkingService

```csharp
public interface ITextChunkingService
{
    List<string> ChunkByParagraph(string text, int minLength = 50);
    List<string> ChunkByFixedSize(string text, int chunkSize = 2000);
    List<string> ChunkBySentence(string text, int maxChunkSize = 2000);
    List<string> ChunkWithOverlap(string text, int chunkSize = 2000, int overlap = 200);
}
```

### Service: IDocumentParsingService

```csharp
public interface IDocumentParsingService
{
    Task<string> ExtractTextFromPdfAsync(Stream fileStream);
    Task<string> ExtractTextFromDocxAsync(Stream fileStream);
    string ExtractTextFromTxt(Stream fileStream);
}
```

### Flow: IKnowledgeBaseChunkingService

```csharp
public interface IKnowledgeBaseChunkingService
{
    Task<bool> ProcessAndChunkDocumentAsync(
        int documentId, 
        Stream fileStream, 
        string fileName,
        string fileType);
}
```

---

## ?? Chunking Comparison

| Strategy | Complexity | Performance | Accuracy | Recommended |
|----------|-----------|-------------|----------|------------|
| **Paragraph** | Low | Fast | Good | ? YES |
| **Fixed Size** | Low | Fast | Fair | Maybe |
| **Semantic** | Medium | Medium | Excellent | Later |
| **Sliding Window** | Medium | Slow | Good | Maybe |

---

## ?? Untuk Implementasi Sekarang (Optional)

Tidak perlu implement chunking sekarang. Bisa:

1. **Upload dokumen via API**
   ```
   POST /api/kb/documents
   - Upload file (PDF/Word/TXT)
   - Store raw di database
   ```

2. **Manual chunking later**
   - Saat ada dokumen, split manual
   - Atau implement automated chunking service

3. **For now:**
   - Use paragraph-level storage
   - One paragraph = one searchable unit

---

## ?? Available NuGet Packages (jika mau)

- **iTextSharp** - Parse PDF
- **DocumentFormat.OpenXml** - Parse Word
- **SharpNLP** - Sentence tokenization
- **OpenAI** - Embeddings generation

Tapi semua ini **optional** - bisa pakai simple string split dulu.

---

## ? Recommendation

**For Now:**
- Implement simple **Paragraph Chunking** (Option 1)
- Manual upload dokumen via API
- Store chunks di SQL Server

**Later (Phase 2):**
- Add PDF/Word parsing
- Automate embedding generation
- Implement vector search optimization

---

**Simple = Better untuk MVP!** ??
