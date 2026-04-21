---
marp: true
theme: default
paginate: true
backgroundColor: #0f172a
color: #f1f5f9
style: |
  section {
    font-family: 'Segoe UI', sans-serif;
  }
  h1 { color: #38bdf8; }
  h2 { color: #7dd3fc; }
  h3 { color: #bae6fd; }
  code { background: #1e293b; color: #a5f3fc; }
  table { width: 100%; }
  th { background: #1e40af; color: white; }
  td { background: #1e293b; }
  strong { color: #fbbf24; }
---

<!-- SLIDE 1 — TITLE -->
# Retrieval-Augmented Generation (RAG)
## A Step-by-Step Deep Dive

**Stack:**
- .NET 10 · ASP.NET Core Minimal API
- OpenAI — `gpt-4o-mini` + `text-embedding-3-small`
- SQL Server — native `VECTOR(1536)` column
- Microsoft.Extensions.AI — vendor-neutral abstractions
- PdfPig — open-source PDF parsing

---

<!-- SLIDE 2 — THE PROBLEM -->
# Why Not Just Send the Whole Document?

The naive approach — pasting the full document into the prompt — fails:

| Problem | Detail |
|---|---|
| **Token limits** | A 300-page PDF can easily exceed 128 k tokens |
| **Cost** | 100 000 tokens per request × every query |
| **Accuracy** | "Lost in the middle" — LLMs miss content buried deep in long contexts |

> LLMs are trained on a **fixed snapshot** of the world.  
> They cannot know your private documents.

---

<!-- SLIDE 3 — THE SOLUTION -->
# RAG Solves All Three Problems

Instead of sending the whole document, **retrieve only the relevant pieces first**.

```
Without RAG:   Question + 300-page doc  →  LLM  →  Answer
With RAG:      Question + top-5 chunks  →  LLM  →  Answer
```

**Two phases:**

```
[Ingestion Phase]              [Query Phase]
Documents → Vectors            Question → Retrieve → Generate → Answer
   (done once)                    (done per user request)
```

---

<!-- SLIDE 4 — ARCHITECTURE OVERVIEW -->
# High-Level Architecture

```
Client
  │
  ├── POST /api/documents/upload   →   Ingestion Pipeline
  │
  └── POST /api/chat/ask           →   RAG Query Pipeline
```

| Service | Responsibility |
|---|---|
| `PdfProcessingService` | Extract + chunk PDF text |
| `EmbeddingService` | Call OpenAI embeddings API |
| `VectorSearchService` | Cosine search in SQL Server |
| `RagService` | Orchestrate retrieval + LLM call |

---

<!-- SLIDE 5 — PHASE 1 OVERVIEW -->
# Phase 1 — Document Ingestion Pipeline

> Runs **once per uploaded PDF**

```
 PDF File
    │
   [1] Validate & save to disk (GUID filename)
    │
   [2] PdfPig extracts text page-by-page
    │
   [3] Split into overlapping chunks (1000 / 200)
    │
   [4] INSERT Document + DocumentChunks → SQL Server
    │
   [5] Embed chunks (batch 20) → store VECTOR(1536)
    │
  201 Created
```

---

<!-- SLIDE 6 — STEP 1: RECEIVE FILE -->
# Step 1 — Receive & Persist the File

**Endpoint:** `POST /api/documents/upload`

- Validates: must be `.pdf`, non-empty
- Saves to `wwwroot/uploads/` with a **GUID filename** to prevent collisions

```csharp
var savedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
var filePath = Path.Combine(uploadsDir, savedFileName);

await using var stream = new FileStream(filePath, FileMode.Create);
await file.CopyToAsync(stream);
```

**Why disk first?**  
The PDF stream needs to be opened multiple times — buffering to disk avoids loading into memory.

---

<!-- SLIDE 7 — STEP 2: EXTRACT TEXT -->
# Step 2 — Extract Text from PDF

**Library:** [PdfPig](https://github.com/UglyToad/PdfPig) — pure .NET, no native deps

```csharp
using var document = PdfDocument.Open(pdfStream);

foreach (Page page in document.GetPages())
{
    var text = page.Text;
    if (string.IsNullOrWhiteSpace(text)) continue;

    var chunks = SplitIntoChunks(text.Trim());
    allChunks.Add((chunk, page.Number)); // tagged with page number!
}
```

- Iterates every page
- Skips image-only / blank pages
- Tags each chunk with **page number** → used for citations later

---

<!-- SLIDE 8 — STEP 3: CHUNKING -->
# Step 3 — Split into Overlapping Chunks

```
MaxChunkSize = 1000 characters
OverlapSize  =  200 characters
```

**Why overlap?** A key sentence at a 1000-char boundary would be split across two chunks.  
Overlap ensures it appears fully in at least one.

```
Chunk 1: ████████████████████  (1000 chars)
                       ████████████████████  Chunk 2
                  ▲────▲
             200-char overlap
```

```csharp
start += MaxChunkSize - OverlapSize; // advance 800, not 1000
```

---

<!-- SLIDE 9 — STEP 4: PERSIST TO DATABASE -->
# Step 4 — Persist to SQL Server

Two-step save (need DB-generated IDs before inserting vectors):

```csharp
// 1. Insert Document row
db.Documents.Add(document);
await db.SaveChangesAsync(); // ← generates document.Id

// 2. Insert chunks (Embedding = NULL for now)
db.DocumentChunks.AddRange(chunkEntities);
await db.SaveChangesAsync();
```

**Schema:**
```
DocumentChunks
├── Id           INT IDENTITY (PK)
├── DocumentId   INT (FK)
├── Content      NVARCHAR(MAX)
├── PageNumber   INT
└── Embedding    VECTOR(1536)  ← native SQL Server vector column
```

---

<!-- SLIDE 10 — STEP 5: GENERATE EMBEDDINGS -->
# Step 5 — Generate & Store Embeddings

**Service:** `EmbeddingService` · **Model:** `text-embedding-3-small`

Each chunk → **1536-dimensional float vector** encoding its semantic meaning.

```csharp
// Batches of 20 to respect API rate limits
for (var i = 0; i < texts.Count; i += 20)
{
    var batch = texts.Skip(i).Take(20).ToList();
    var embeddings = await embeddingGenerator.GenerateAsync(batch);
    result.AddRange(embeddings.Select(e => e.Vector.ToArray()));
}
```

```csharp
// Persist using SQL Server native VECTOR type
var sqlVector = new SqlVector<float>(embedding);
await db.DocumentChunks
    .Where(c => c.Id == chunkId)
    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Embedding, sqlVector));
```

---

<!-- SLIDE 11 — WHAT IS AN EMBEDDING? -->
# What Is a Vector Embedding?

A 1536-dimensional point in mathematical space where **meaning** determines position.

```
"What is the revenue?"   →  [0.012, -0.847, 0.334, ... ]  (1536 floats)
"Annual income figures"  →  [0.018, -0.841, 0.329, ... ]  ← very close!
"The cat sat on a mat"   →  [0.723,  0.102, -0.551, ... ] ← very far
```

> Texts with **similar meaning** end up **close together** in vector space —  
> regardless of the exact words used.

This is what makes semantic search possible.

---

<!-- SLIDE 12 — PHASE 2 OVERVIEW -->
# Phase 2 — Query Pipeline

> Runs **per user question**

```
 User question
      │
     [1] Embed the question → float[1536]
      │
     [2] VECTOR_DISTANCE cosine search → top-5 chunks
      │
     [3] Build grounding context with source tags
      │
     [4] Construct System + User messages
      │
     [5] IChatClient → gpt-4o-mini
      │
     [6] 200 OK { answer, sources[] }
```

---

<!-- SLIDE 13 — STEP 6: EMBED THE QUESTION -->
# Step 6 — Embed the User's Question

**Critical rule:** use the **exact same model** as ingestion.  
Different models produce incompatible vector spaces — cosine distances would be meaningless.

```csharp
var queryEmbedding = await embeddingService.GetEmbeddingAsync(question);
// float[1536] — same space as stored chunk vectors
```

- Fast: < 100 ms
- Cheap: fraction of a chat completion cost
- Single API call regardless of document count

---

<!-- SLIDE 14 — STEP 7: VECTOR SEARCH -->
# Step 7 — Cosine Vector Search

`VectorSearchService` runs a ranked SQL query using SQL Server's native `VECTOR_DISTANCE`:

```csharp
var sqlVector = new SqlVector<float>(queryEmbedding);

db.DocumentChunks
  .Select(c => new {
      // ...
      Distance = EF.Functions.VectorDistance("cosine", c.Embedding, sqlVector)
  })
  .OrderBy(c => c.Distance)   // ascending = most similar first
  .Take(5)
```

| Metric | Formula |
|---|---|
| Cosine distance | `1 − (A · B) / (‖A‖ · ‖B‖)` |
| Relevance score | `1 − distance` (0 = unrelated, 1 = identical) |

---

<!-- SLIDE 15 — STEP 8: BUILD THE PROMPT -->
# Step 8 — Build the Grounding Prompt

Retrieved chunks are assembled into a numbered source list:

```
[Source 1: annual_report.pdf, Page 12]
Net revenue for fiscal year 2023 was $4.2 billion...

[Source 2: annual_report.pdf, Page 13]
Operating expenses were $1.8 billion...
```

**System message constrains the model:**

```
You are a document assistant.
Answer questions ONLY from the provided sources.
If the sources are insufficient, say so clearly.
```

> Without this constraint, the LLM fills gaps with training data — **hallucination**.

---

<!-- SLIDE 16 — STEP 9: GENERATE THE ANSWER -->
# Step 9 — Generate the Answer with the LLM

```csharp
var messages = new List<ChatMessage>
{
    new(ChatRole.System, systemPrompt),
    new(ChatRole.User,   $"Sources:\n{context}\n\nQuestion: {question}")
};

var response = await chatClient.GetResponseAsync(messages);
var answer   = response.Text;
```

`IChatClient` is a **vendor-neutral abstraction** from `Microsoft.Extensions.AI`.

```csharp
// Program.cs — swap model here without touching service code
builder.Services.AddChatClient(
    openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());
```

---

<!-- SLIDE 17 — STEP 10: RETURN WITH CITATIONS -->
# Step 10 — Return Answer + Source Citations

```json
{
  "answer": "Net revenue for FY2023 was $4.2 billion, a 14% year-over-year increase.",
  "sources": [
    { "documentName": "annual_report.pdf", "pageNumber": 12, "relevance": 0.9312 },
    { "documentName": "annual_report.pdf", "pageNumber": 13, "relevance": 0.8874 }
  ]
}
```

- **`relevance`** — how confident the retrieval was (0–1)
- Users can **verify answers** against the original document page
- Builds **trust** and reduces hallucination risk perception

---

<!-- SLIDE 18 — KEY DESIGN DECISIONS -->
# Key Design Decisions

| Decision | Why |
|---|---|
| SQL Server `VECTOR` type | Single DB, no extra infra. Scales to millions of chunks. |
| 200-char overlap | Prevents information loss at chunk boundaries |
| Same embedding model always | Cosine similarity only works in the same vector space |
| `IChatClient` / `IEmbeddingGenerator` | Swap OpenAI → Azure / Ollama by changing one line |
| Batch size 20 | Stays within OpenAI rate limits with minimal latency |
| System prompt constraint | Primary defence against hallucination |
| Source citations in response | Verifiable answers, increased user trust |

---

<!-- SLIDE 19 — COMPLETE FLOW -->
# Complete Data Flow

```
═══ INGESTION ══════════════════════════════════════
PDF upload → validate → save to disk
         → PdfPig extract pages
         → split chunks (1000/200 overlap)
         → INSERT Document + DocumentChunks
         → OpenAI embed (batch 20) → float[1536]
         → UPDATE Embedding = SqlVector<float>
         → 201 Created

═══ QUERY ══════════════════════════════════════════
POST /ask  → embed question → float[1536]
          → VECTOR_DISTANCE cosine → top-5 chunks
          → build [Source N: file, Page X] context
          → ChatMessage(System) + ChatMessage(User)
          → gpt-4o-mini → answer text
          → 200 OK { answer, sources[] }
```

---

<!-- SLIDE 20 — SUMMARY -->
# Summary

**RAG = Retrieve first, then Generate**

| Phase | Steps | Key Technology |
|---|---|---|
| **Ingestion** | 1–5 | PdfPig, `IEmbeddingGenerator`, `VECTOR(1536)` |
| **Query** | 6–10 | `VECTOR_DISTANCE`, `IChatClient`, GPT-4o-mini |

**Key takeaway:**  
The embedding model is the **bridge** between ingestion and query.  
Use the **same model** for both — always.

---

<!-- SLIDE 21 — TECH STACK -->
# Tech Stack Reference

| Layer | Technology |
|---|---|
| API | ASP.NET Core Minimal API (.NET 10) |
| LLM | OpenAI `gpt-4o-mini` |
| Embeddings | OpenAI `text-embedding-3-small` (1536-dim) |
| Vector Store | SQL Server — native `VECTOR(1536)` column |
| PDF Parsing | PdfPig |
| AI Abstractions | `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) |
| ORM | Entity Framework Core + `EF.Functions.VectorDistance` |

---

<!-- SLIDE 22 — CLOSING -->
# Thank You

**Repository:** `Fcakiroglu16/MicrosoftAgentFrameworkPlayground`

**Resources:**
- [Microsoft.Extensions.AI docs](https://learn.microsoft.com/en-us/dotnet/ai/ai-extensions)
- [SQL Server Vector Support](https://learn.microsoft.com/en-us/sql/relational-databases/vectors/vectors-sql-server)
- [OpenAI Embeddings Guide](https://platform.openai.com/docs/guides/embeddings)
- [PdfPig GitHub](https://github.com/UglyToad/PdfPig)

> *Built with .NET 10 · OpenAI · SQL Server · Microsoft.Extensions.AI · PdfPig*
