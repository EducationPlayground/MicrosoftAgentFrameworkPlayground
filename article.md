# Building a RAG-Powered Document Q&A API with .NET, OpenAI, and SQL Server

## Introduction

This project is a **Retrieval-Augmented Generation (RAG)** system built on ASP.NET Core (.NET 10). It lets you upload PDF documents and then ask natural language questions about their content. Instead of sending entire documents to a language model — which is expensive and hits token limits — the system retrieves only the most relevant passages before generating an answer.

The stack:
- **ASP.NET Core Minimal API** — lightweight HTTP endpoints
- **OpenAI** — `gpt-4o-mini` for chat, `text-embedding-3-small` for vector embeddings
- **SQL Server** — stores documents, chunks, and 1536-dimensional embedding vectors using the native `vector` column type
- **PdfPig** — open-source .NET library for parsing PDF text
- **Microsoft.Extensions.AI** — vendor-neutral AI abstractions (`IChatClient`, `IEmbeddingGenerator`)

---

## Architecture Overview

```
Client
  │
  ├── POST /api/documents/upload   →  Ingest Pipeline
  │
  └── POST /api/chat/ask           →  RAG Query Pipeline
```

The system has two distinct flows: **document ingestion** and **question answering**.

---

## Part 1: Document Ingestion Pipeline

When a user uploads a PDF, the following five steps happen in sequence.

### Step 1 — Receive and Persist the File

The `POST /api/documents/upload` endpoint accepts a multipart form upload. After basic validation (must be a `.pdf` file with non-zero length), the file is saved to an `Uploads/` folder on disk with a new GUID-based filename to prevent collisions.

```csharp
var savedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
var filePath = Path.Combine(uploadsDir, savedFileName);
await using var stream = new FileStream(filePath, FileMode.Create);
await file.CopyToAsync(stream);
```

### Step 2 — Extract Text and Split into Chunks

`PdfProcessingService` uses **PdfPig** to iterate over every page and extract its raw text. Because embedding models have token limits and vector search works best on focused passages, the text is then split into overlapping chunks.

```
MaxChunkSize  = 1000 characters
OverlapSize   = 200 characters
```

The 200-character overlap means adjacent chunks share a small window of text, so information that falls on a boundary is not lost.

```
 |<---- 1000 ---->|
              |<---- 1000 ---->|
         (200 overlap)
```

The result is a flat list of `(Content, PageNumber)` tuples.

### Step 3 — Save the Document Record and Chunk Records

A `Document` entity (filename, upload time, page count, total chunk count) is inserted first. Then all `DocumentChunk` entities are bulk-inserted, each referencing the parent document and recording its page number and position index. At this point the `Embedding` column is still `NULL`.

### Step 4 — Generate Embeddings in Batches

`EmbeddingService` wraps `IEmbeddingGenerator<string, Embedding<float>>`, which is backed by OpenAI's `text-embedding-3-small` model. To stay within API rate limits, chunks are processed in batches of 20:

```csharp
const int batchSize = 20;
for (var i = 0; i < textList.Count; i += batchSize)
{
    var batch = textList.Skip(i).Take(batchSize).ToList();
    var embeddings = await embeddingGenerator.GenerateAsync(batch);
    // ...
}
```

Each chunk produces a `float[]` of **1536 dimensions**.

### Step 5 — Store Vectors in SQL Server

`VectorSearchService.SaveChunkWithEmbeddingAsync` wraps the `float[]` in a `SqlVector<float>` and uses an `ExecuteUpdateAsync` to write the value into the `vector(1536)` column — a native SQL Server vector type:

```sql
-- Schema (defined via EF Core fluent API)
Embedding vector(1536)
```

The endpoint returns a `201 Created` response with document metadata (ID, filename, page count, chunk count, upload timestamp).

---

## Part 2: Question Answering Pipeline (RAG)

When a user posts a question to `POST /api/chat/ask`, `RagService.AskAsync` orchestrates four steps.

### Step 1 — Embed the Question

The user's question is converted to the same 1536-dimensional vector space using the identical embedding model (`text-embedding-3-small`). This is what makes semantic similarity comparison meaningful — both the question and the stored chunks live in the same mathematical space.

```csharp
var queryEmbedding = await embeddingService.GetEmbeddingAsync(question);
```

### Step 2 — Retrieve the Top-N Most Relevant Chunks

`VectorSearchService.SearchSimilarChunksAsync` issues a LINQ query that uses `EF.Functions.VectorDistance("cosine", ...)` — a SQL Server built-in function — to rank every chunk by cosine distance to the query embedding and return the 5 closest:

```csharp
.OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding.GetValueOrDefault(), sqlVector))
.Take(topN)
```

Relevance is computed as `1 - cosine_distance`, so a score of `1.0` means identical and `0.0` means completely unrelated.

The query also supports an optional `documentId` filter, so users can scope the search to a specific uploaded document.

### Step 3 — Build a Context Window

The retrieved chunks are assembled into a numbered source list:

```
[Kaynak 1: annual_report.pdf, Sayfa 4]
... chunk text ...

[Kaynak 2: annual_report.pdf, Sayfa 5]
... chunk text ...
```

This becomes the **grounding context** injected into the LLM prompt.

### Step 4 — Generate the Answer with GPT-4o-mini

A two-message conversation is constructed:

| Role   | Content |
|--------|---------|
| System | "You are a corporate document assistant. Answer only from the provided sources. If the sources are insufficient, say so." |
| User   | Retrieved sources + the user's question |

`IChatClient.GetResponseAsync` sends this to `gpt-4o-mini` and returns the generated text. The model is explicitly constrained to only use the provided sources, which prevents hallucination about content not in the uploaded documents.

The API response includes both the **answer text** and a **source reference list** (document name, page number, relevance score), letting the client show citations.

---

## Data Model

```
Documents
├── Id (PK)
├── FileName          (GUID-based, stored on disk)
├── OriginalFileName  (user-visible name)
├── UploadedAt
├── PageCount
└── TotalChunks

DocumentChunks
├── Id (PK)
├── DocumentId (FK → Documents)
├── ChunkIndex
├── Content (nvarchar(max))
├── PageNumber
└── Embedding (vector(1536))   ← native SQL Server vector column
```

Deleting a document cascades to all its chunks.

---

## Dependency Injection Setup

Everything is wired in `Program.cs` using the `Microsoft.Extensions.AI` registration helpers:

```csharp
builder.Services.AddChatClient(
    openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());

builder.Services.AddEmbeddingGenerator(
    openAiClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator());
```

Because the AI abstractions are registered as interfaces (`IChatClient`, `IEmbeddingGenerator`), swapping out OpenAI for another provider (Azure OpenAI, Ollama, etc.) requires changing only these two lines.

---

## End-to-End Flow Summary

```
Upload PDF
    │
    ▼
PdfPig extracts text per page
    │
    ▼
Text split into overlapping 1000-char chunks
    │
    ▼
Chunks saved to SQL Server (Embedding = NULL)
    │
    ▼
OpenAI text-embedding-3-small → float[1536] per chunk
    │
    ▼
Embeddings written to vector(1536) column
    │
    ─────────────────────────────────────────
Ask Question
    │
    ▼
Question embedded → float[1536]
    │
    ▼
SQL Server cosine distance search → top 5 chunks
    │
    ▼
Chunks assembled into LLM prompt context
    │
    ▼
GPT-4o-mini generates grounded answer
    │
    ▼
Answer + source citations returned to client
```

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| SQL Server `vector` type instead of a dedicated vector DB | Keeps the infrastructure simple — one database for both relational data and vectors |
| Chunking with overlap | Prevents information loss at chunk boundaries |
| `Microsoft.Extensions.AI` abstractions | Provider-agnostic; easy to swap OpenAI for another model |
| Minimal API endpoints | Less boilerplate than controllers; suitable for a focused API surface |
| Batch embedding (20 at a time) | Respects OpenAI API rate limits |
| Source citations in the response | Enables the client to show verifiable references, increasing trust |
