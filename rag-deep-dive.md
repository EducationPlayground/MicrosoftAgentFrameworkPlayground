# Retrieval-Augmented Generation (RAG): A Step-by-Step Deep Dive

> This article walks through every step of a production-ready RAG pipeline implemented in .NET 10,
> using OpenAI embeddings and `gpt-4o-mini`, SQL Server as the vector store,
> and `Microsoft.Extensions.AI` as the vendor-neutral abstraction layer.

---

## Table of Contents

1. [What Is RAG and Why Do We Need It?](#1-what-is-rag-and-why-do-we-need-it)
2. [High-Level Architecture](#2-high-level-architecture)
3. [Phase 1 — Document Ingestion Pipeline](#3-phase-1--document-ingestion-pipeline)
   - [Step 1: Receive and Persist the File](#step-1-receive-and-persist-the-file)
   - [Step 2: Extract Text from the PDF](#step-2-extract-text-from-the-pdf)
   - [Step 3: Split Text into Overlapping Chunks](#step-3-split-text-into-overlapping-chunks)
   - [Step 4: Persist Documents and Chunks to the Database](#step-4-persist-documents-and-chunks-to-the-database)
   - [Step 5: Generate Embeddings and Store Vectors](#step-5-generate-embeddings-and-store-vectors)
4. [Phase 2 — Query Pipeline (Retrieval + Generation)](#4-phase-2--query-pipeline-retrieval--generation)
   - [Step 6: Embed the User's Question](#step-6-embed-the-users-question)
   - [Step 7: Retrieve the Most Relevant Chunks (Vector Search)](#step-7-retrieve-the-most-relevant-chunks-vector-search)
   - [Step 8: Build the Grounding Prompt](#step-8-build-the-grounding-prompt)
   - [Step 9: Generate the Answer with the LLM](#step-9-generate-the-answer-with-the-llm)
   - [Step 10: Return the Answer with Source Citations](#step-10-return-the-answer-with-source-citations)
5. [Why Each Design Decision Was Made](#5-why-each-design-decision-was-made)
6. [Complete Data Flow Diagram](#6-complete-data-flow-diagram)

---

## 1. What Is RAG and Why Do We Need It?

A large language model (LLM) is trained on a fixed snapshot of the world. It cannot know about:

- Your company's internal documents
- Files uploaded after its training cut-off
- Private data that was never part of its training corpus

The naive solution — paste the entire document into the prompt — fails for three reasons:

| Problem | Detail |
|---|---|
| **Token limits** | Most models accept 4 k–128 k tokens. A 300-page PDF can easily exceed this. |
| **Cost** | Input tokens are billed per request. Sending 100 000 tokens every query is expensive. |
| **Accuracy** | LLMs suffer from the "lost in the middle" problem — they attend poorly to content buried in very long contexts. |

**RAG solves all three problems** by retrieving only the small, most relevant passages before calling the model.

The process has two phases:

```
[Ingestion Phase]           [Query Phase]
Documents → Vectors         Question → Retrieve → Generate → Answer
   (done once)                (done per user request)
```

---

## 2. High-Level Architecture

```
┌──────────────────────────────────────────────────────────┐
│                        Client                            │
└──────┬───────────────────────────────────┬───────────────┘
       │                                   │
       ▼                                   ▼
POST /api/documents/upload        POST /api/chat/ask
       │                                   │
       ▼                                   ▼
┌─────────────────┐             ┌──────────────────────┐
│  Ingestion      │             │  RAG Query           │
│  Pipeline       │             │  Pipeline            │
│                 │             │                      │
│  PdfPig         │             │  EmbeddingService    │
│  → chunks       │             │  → query vector      │
│  → embeddings   │             │                      │
│  → SQL Server   │             │  VectorSearchService │
└─────────────────┘             │  → top-5 chunks      │
                                │                      │
                                │  RagService          │
                                │  → LLM prompt        │
                                │  → GPT-4o-mini       │
                                └──────────────────────┘
```

Both phases share the same SQL Server database, and both use the same embedding model (`text-embedding-3-small`) to ensure vectors are in the same mathematical space.

---

## 3. Phase 1 — Document Ingestion Pipeline

The ingestion phase runs **once per document**. Its goal is to transform a raw PDF into a searchable vector index stored in SQL Server.

### Step 1: Receive and Persist the File

**Endpoint:** `POST /api/documents/upload`

The client sends a multipart/form-data request containing the PDF file.
The endpoint performs two validation checks before touching the file system:

```csharp
if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    return Results.BadRequest("Only PDF files can be uploaded.");
```

If validation passes, the file is written to the `wwwroot/uploads/` directory using a GUID-based filename to avoid collisions between identically-named uploads:

```csharp
var savedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
var filePath = Path.Combine(uploadsDir, savedFileName);

await using var stream = new FileStream(filePath, FileMode.Create);
await file.CopyToAsync(stream);
```

**Why save to disk first?** The PDF stream needs to be opened twice — once for text extraction and potentially again for other operations. Buffering to disk avoids loading the entire file into memory.

---

### Step 2: Extract Text from the PDF

**Service:** `PdfProcessingService` using [PdfPig](https://github.com/UglyToad/PdfPig)

`PdfPig` opens the PDF stream and iterates over every page, extracting the raw text content:

```csharp
using var document = PdfDocument.Open(pdfStream);
var pageCount = document.NumberOfPages;

foreach (Page page in document.GetPages())
{
    var text = page.Text;
    if (string.IsNullOrWhiteSpace(text))
        continue;

    var chunks = SplitIntoChunks(text.Trim());
    foreach (var chunk in chunks)
    {
        allChunks.Add((chunk, page.Number));  // (text, pageNumber)
    }
}
```

Each chunk is tagged with its **source page number** so citations can be shown later. Pages that contain only images or whitespace are skipped.

**Why PdfPig?** It is a pure .NET, open-source library with no native dependencies, making it easy to deploy anywhere without additional system packages.

---

### Step 3: Split Text into Overlapping Chunks

Raw page text can be thousands of characters long — too large for meaningful embedding. The text is split into fixed-size windows with a deliberate overlap:

```
MaxChunkSize = 1000 characters
OverlapSize  =  200 characters
```

```csharp
private static List<string> SplitIntoChunks(string text)
{
    var chunks = new List<string>();
    if (text.Length <= MaxChunkSize)
    {
        chunks.Add(text);
        return chunks;
    }

    var start = 0;
    while (start < text.Length)
    {
        var length = Math.Min(MaxChunkSize, text.Length - start);
        chunks.Add(text.Substring(start, length));
        start += MaxChunkSize - OverlapSize; // advance by 800, not 1000
    }

    return chunks;
}
```

**Why overlap?** Imagine a key sentence that falls exactly at a 1000-character boundary. Without overlap, it would be split across two chunks, making it hard for vector search to find either one confidently. The 200-character overlap ensures every piece of information appears fully in at least one chunk.

```
Chunk 1: ████████████████████ (1000 chars)
                          ████████████████████ Chunk 2
                     ▲────▲
                  200-char overlap
```

---

### Step 4: Persist Documents and Chunks to the Database

Before generating embeddings, the document record and all chunk records are written to SQL Server:

```csharp
// 1. Insert the document
var document = new Document
{
    FileName        = savedFileName,        // GUID filename on disk
    OriginalFileName = file.FileName,       // user-visible name
    UploadedAt      = DateTime.UtcNow,
    PageCount       = pageCount,
    TotalChunks     = chunks.Count
};
db.Documents.Add(document);
await db.SaveChangesAsync(); // ← generates document.Id

// 2. Insert all chunks (Embedding column = NULL at this point)
var chunkEntities = chunks.Select((c, i) => new DocumentChunk
{
    DocumentId  = document.Id,
    ChunkIndex  = i,
    Content     = c.Content,
    PageNumber  = c.PageNumber
}).ToList();

db.DocumentChunks.AddRange(chunkEntities);
await db.SaveChangesAsync();
```

At this point every chunk exists in the database with `Embedding = NULL`. The two-step save is intentional: we need the chunk `Id` values (generated by the database) before we can store their corresponding vectors.

**Database schema:**

```
Documents
├── Id              INT IDENTITY (PK)
├── FileName        NVARCHAR     (GUID-based, stored on disk)
├── OriginalFileName NVARCHAR    (user-visible)
├── UploadedAt      DATETIME2
├── PageCount       INT
└── TotalChunks     INT

DocumentChunks
├── Id              INT IDENTITY (PK)
├── DocumentId      INT          (FK → Documents, CASCADE DELETE)
├── ChunkIndex      INT
├── Content         NVARCHAR(MAX)
├── PageNumber      INT
└── Embedding       VECTOR(1536)  ← native SQL Server vector column
```

---

### Step 5: Generate Embeddings and Store Vectors

**Service:** `EmbeddingService` wrapping `IEmbeddingGenerator<string, Embedding<float>>`

This is the most computationally expensive step. Each chunk's text is sent to the OpenAI Embeddings API (`text-embedding-3-small`) which returns a 1536-dimensional vector of `float` values.

To avoid hitting API rate limits, chunks are processed in batches of 20:

```csharp
const int batchSize = 20;
for (var i = 0; i < textList.Count; i += batchSize)
{
    var batch = textList.Skip(i).Take(batchSize).ToList();
    var embeddings = await embeddingGenerator.GenerateAsync(batch);

    foreach (var embedding in embeddings)
    {
        result.Add(embedding.Vector.ToArray()); // float[1536]
    }
}
```

Each `float[1536]` vector is then persisted using SQL Server's native `VECTOR` type via `SqlVector<float>`:

```csharp
// VectorSearchService.SaveChunkWithEmbeddingAsync
var sqlVector = new SqlVector<float>(embedding);

await db.DocumentChunks
    .Where(c => c.Id == chunkId)
    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Embedding, sqlVector));
```

**What does a 1536-dimensional vector represent?**

It is a point in a 1536-dimensional mathematical space where the position encodes the *semantic meaning* of the text. Texts that mean similar things end up close to each other in this space — regardless of the exact words used. This is what makes similarity search possible.

```
"What is the revenue?"  →  [0.012, -0.847, 0.334, ... ] (1536 floats)
"Annual income figures" →  [0.018, -0.841, 0.329, ... ] (1536 floats, very similar)
"The cat sat on a mat" →  [0.723,  0.102, -0.551, ... ] (1536 floats, very different)
```

---

## 4. Phase 2 — Query Pipeline (Retrieval + Generation)

The query phase runs **once per user question**. Its goal is to find the relevant chunks in the vector store and use them to generate a grounded, citation-backed answer.

### Step 6: Embed the User's Question

**Endpoint:** `POST /api/chat/ask`  
**Service:** `RagService` → `EmbeddingService`

The question must be converted into the same 1536-dimensional vector space as the stored chunks — using the **exact same embedding model**. If you used a different model to embed the question, the distance calculations would be meaningless.

```csharp
public async Task<(string Answer, List<ChunkSearchResult> Sources)> AskAsync(
    string question, int? documentId = null)
{
    // Step 1: Embed the question
    var queryEmbedding = await embeddingService.GetEmbeddingAsync(question);
    // queryEmbedding is float[1536]
```

This single API call is fast (< 100 ms) and cheap — embedding generation costs a fraction of a chat completion.

---

### Step 7: Retrieve the Most Relevant Chunks (Vector Search)

**Service:** `VectorSearchService.SearchSimilarChunksAsync`

With the question vector in hand, a SQL Server query ranks every stored chunk by **cosine distance** to the question vector and returns the 5 closest:

```csharp
var sqlVector = new SqlVector<float>(queryEmbedding);

return await db.DocumentChunks
    .Where(c => c.Embedding != null
             && (!documentId.HasValue || c.DocumentId == documentId))
    .Select(c => new
    {
        // ...
        Distance = EF.Functions.VectorDistance("cosine", c.Embedding.GetValueOrDefault(), sqlVector)
    })
    .OrderBy(c => c.Distance)   // ascending = most similar first
    .Take(topN)                 // default topN = 5
    .Select(c => new ChunkSearchResult(
        c.Id, c.DocumentId, c.Content, c.PageNumber,
        c.ChunkIndex, c.OriginalFileName,
        Relevance: 1 - c.Distance)) // 1.0 = identical, 0.0 = unrelated
    .ToListAsync();
```

**Cosine similarity vs. cosine distance:**

- **Cosine distance** = `1 − (A · B) / (‖A‖ · ‖B‖)` — ranges from 0 (identical) to 2 (opposite)
- **Relevance score** = `1 − distance` — higher is better, shown to the client

The optional `documentId` filter lets users scope the search to a single uploaded document, which is useful when a knowledge base has many documents and the user knows which one is relevant.

---

### Step 8: Build the Grounding Prompt

**Service:** `RagService`

The retrieved chunks are assembled into a numbered source list that becomes the **grounding context** for the LLM:

```csharp
var contextBuilder = new StringBuilder();
for (var i = 0; i < relevantChunks.Count; i++)
{
    var chunk = relevantChunks[i];
    contextBuilder.AppendLine($"[Source {i + 1}: {chunk.DocumentName}, Page {chunk.PageNumber}]");
    contextBuilder.AppendLine(chunk.Content);
    contextBuilder.AppendLine();
}
```

The resulting context looks like:

```
[Source 1: annual_report.pdf, Page 12]
Net revenue for fiscal year 2023 was $4.2 billion, representing a 14% increase...

[Source 2: annual_report.pdf, Page 13]
Operating expenses were $1.8 billion. The growth was primarily driven by...
```

This context is then injected into a two-message conversation:

```csharp
var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
        You are a corporate document assistant.
        Answer questions only from the provided sources.
        If the sources are insufficient to answer, say so clearly.
        """),

    new(ChatRole.User, $"""
        Sources:
        {contextBuilder}

        Question: {question}
        """)
};
```

**Why a system prompt constraint?** Without it, the LLM will fill gaps in the retrieved context with its training data — confidently stating things that are not in the document. The system prompt acts as a guardrail against hallucination.

---

### Step 9: Generate the Answer with the LLM

**Service:** `RagService` → `IChatClient`

The messages list is passed to `IChatClient.GetResponseAsync`, which sends them to `gpt-4o-mini`:

```csharp
var response = await chatClient.GetResponseAsync(messages);
var answer = response.Text;
```

`IChatClient` is an abstraction from `Microsoft.Extensions.AI`. The concrete implementation — OpenAI's `ChatClient` — is registered in `Program.cs`:

```csharp
builder.Services.AddChatClient(
    openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());
```

Because the application code only depends on `IChatClient`, the underlying model can be swapped for Azure OpenAI, Ollama, Anthropic, or any other provider by changing only the registration line — no service code changes required.

---

### Step 10: Return the Answer with Source Citations

**Endpoint:** `ChatEndpoints.Ask`

The response payload includes both the generated text and a source reference list so the client can display verifiable citations:

```csharp
var sourceRefs = sources.Select(s => new SourceReference(
    s.DocumentName,
    s.PageNumber,
    Math.Round(s.Relevance, 4)
)).ToList();

return Results.Ok(new AskQuestionResponse(answer, sourceRefs));
```

**Example API response:**

```json
{
  "answer": "Net revenue for fiscal year 2023 was $4.2 billion, a 14% increase year-over-year.",
  "sources": [
    { "documentName": "annual_report.pdf", "pageNumber": 12, "relevance": 0.9312 },
    { "documentName": "annual_report.pdf", "pageNumber": 13, "relevance": 0.8874 }
  ]
}
```

The `relevance` field (0–1) tells the client how confident the retrieval was for each source.

---

## 5. Why Each Design Decision Was Made

| Decision | Why |
|---|---|
| **SQL Server `vector` type** instead of a dedicated vector DB (Pinecone, Qdrant) | Keeps the infrastructure to a single database; no additional service to deploy, monitor, or pay for. SQL Server's `VECTOR_DISTANCE()` is sufficient for tens of millions of chunks. |
| **Overlapping chunks** (1000 chars, 200 overlap) | Ensures sentences near chunk boundaries appear fully in at least one chunk, preventing information loss that would degrade retrieval quality. |
| **Same embedding model** for ingestion and query | Cosine similarity only works when both vectors are from the same model and the same version. Changing the model invalidates all stored vectors. |
| **`Microsoft.Extensions.AI` abstractions** (`IChatClient`, `IEmbeddingGenerator`) | All service code is provider-agnostic. Switching from OpenAI to Azure OpenAI or Ollama requires changing only `Program.cs` registration — zero business logic changes. |
| **Batch embedding in groups of 20** | OpenAI's Embeddings API has rate limits. Processing in small batches adds minimal latency while staying well within those limits. |
| **System prompt constraint** | Prevents the LLM from hallucinating content not present in retrieved chunks — the primary failure mode in RAG systems. |
| **Source citations in the response** | Enables the client to show verifiable references, increasing user trust and making it easy to validate the answer against the original document. |
| **Minimal API** instead of MVC controllers | Less boilerplate for a focused API surface; endpoint logic stays co-located and readable without ceremony. |

---

## 6. Complete Data Flow Diagram

```
═══════════════════════════════════════════════════════
  INGESTION PHASE  (runs once per PDF)
═══════════════════════════════════════════════════════

  Client
    │  POST /api/documents/upload  (multipart PDF)
    ▼
  DocumentEndpoints.UploadDocument
    │
    ├─[1]─ Validate (PDF, non-empty)
    │
    ├─[2]─ Save file to wwwroot/uploads/{guid}.pdf
    │
    ├─[3]─ PdfProcessingService.ExtractChunks
    │        └─ PdfPig reads each page
    │        └─ SplitIntoChunks (1000 chars, 200 overlap)
    │        └─ returns List<(Content, PageNumber)>
    │
    ├─[4]─ INSERT Document  →  SQL Server
    │        INSERT DocumentChunks (Embedding = NULL)
    │
    ├─[5]─ EmbeddingService.GetEmbeddingsAsync
    │        └─ batches of 20 → OpenAI text-embedding-3-small
    │        └─ returns float[1536] per chunk
    │
    ├─[6]─ VectorSearchService.SaveChunkWithEmbeddingAsync
    │        └─ UPDATE DocumentChunks SET Embedding = SqlVector<float>
    │
    └─ 201 Created  →  Client


═══════════════════════════════════════════════════════
  QUERY PHASE  (runs per user question)
═══════════════════════════════════════════════════════

  Client
    │  POST /api/chat/ask  { question, documentId? }
    ▼
  ChatEndpoints.Ask
    │
    ▼
  RagService.AskAsync
    │
    ├─[1]─ EmbeddingService.GetEmbeddingAsync(question)
    │        └─ OpenAI text-embedding-3-small
    │        └─ returns float[1536]  ← query vector
    │
    ├─[2]─ VectorSearchService.SearchSimilarChunksAsync
    │        └─ SELECT ... ORDER BY VECTOR_DISTANCE('cosine', Embedding, @query)
    │        └─ returns top-5 ChunkSearchResult
    │        └─ relevance = 1 - cosine_distance
    │
    ├─[3]─ Build grounding context
    │        └─ "[Source N: file.pdf, Page X]\n{chunk}\n"
    │
    ├─[4]─ Construct messages
    │        └─ ChatMessage(System, constraint prompt)
    │        └─ ChatMessage(User, sources + question)
    │
    ├─[5]─ IChatClient.GetResponseAsync(messages)
    │        └─ OpenAI gpt-4o-mini
    │        └─ returns answer text
    │
    └─ 200 OK  →  { answer, sources: [{ name, page, relevance }] }
```

---

*Built with .NET 10 · ASP.NET Core Minimal API · OpenAI · SQL Server · Microsoft.Extensions.AI · PdfPig*
