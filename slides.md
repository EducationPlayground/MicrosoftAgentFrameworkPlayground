---
marp: true
theme: default
paginate: true
---

<!-- SLIDE 1 — TITLE -->
# Retrieval-Augmented Generation (RAG)
## A Step-by-Step Deep Dive

**Tech Stack:**
- .NET 10 · ASP.NET Core Minimal API
- OpenAI — GPT-4o-mini + text-embedding-3-small
- SQL Server — native vector column (1536 dimensions)
- Microsoft.Extensions.AI — vendor-neutral abstractions
- PdfPig — open-source PDF parsing

---

<!-- SLIDE 2 — THE PROBLEM -->
# Why Not Just Send the Whole Document?

The naive approach — pasting the full document into the prompt — fails:

| Problem | Detail |
|---|---|
| **Token limits** | A 300-page PDF can easily exceed 128 000 tokens |
| **Cost** | Sending 100 000 tokens on every single query is very expensive |
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

**Two endpoints:**

- `POST /api/documents/upload` → Ingestion Pipeline
- `POST /api/chat/ask` → RAG Query Pipeline

**Four services:**

| Service | Responsibility |
|---|---|
| PdfProcessingService | Extract and chunk PDF text |
| EmbeddingService | Call OpenAI embeddings API |
| VectorSearchService | Cosine similarity search in SQL Server |
| RagService | Orchestrate retrieval + LLM call |

---

<!-- SLIDE 5 — PHASE 1 OVERVIEW -->
# Phase 1 — Document Ingestion Pipeline

> Runs **once per uploaded PDF**

```
 PDF File
    │
   [1] Validate & save to disk (unique filename)
    │
   [2] Extract text from PDF, page by page
    │
   [3] Split into overlapping chunks (1000 / 200 chars)
    │
   [4] Save Document and Chunks to SQL Server
    │
   [5] Generate embeddings (batch 20) → store as vectors
    │
  Done — document is searchable
```

---

<!-- SLIDE 6 — STEP 1: RECEIVE FILE -->
# Step 1 — Receive & Persist the File

**Endpoint:** `POST /api/documents/upload`

**What happens:**
- Validates the uploaded file — must be a PDF, must not be empty
- Generates a **unique filename** (GUID) to prevent naming conflicts
- Saves the file to the `uploads/` folder on disk

**Why save to disk first?**

The PDF needs to be read more than once during processing.  
Saving to disk avoids loading the entire file into memory multiple times.

---

<!-- SLIDE 7 — STEP 2: EXTRACT TEXT -->
# Step 2 — Extract Text from PDF

**Library used:** PdfPig — pure .NET library, no native dependencies

**What happens:**
- Opens the PDF file
- Reads every page one by one
- Skips blank pages and image-only pages
- Tags each text block with its **page number**

**Why track the page number?**

Page numbers are stored with each chunk and later returned in the response  
so users can verify the answer in the original document.

---

<!-- SLIDE 8 — STEP 3: CHUNKING -->
# Step 3 — Split into Overlapping Chunks

**Configuration:**

- Max chunk size: **1000 characters**
- Overlap size: **200 characters**

**Why overlap?**

A key sentence that falls exactly at the 1000-character boundary  
would be cut in half and lost between two chunks.

With 200-character overlap, that sentence appears fully in at least one chunk.

```
Chunk 1: ████████████████████  (1000 chars)
                       ████████████████████  Chunk 2
                  ▲────▲
             200-char overlap
```

Each step advances **800 characters**, not 1000.

---

<!-- SLIDE 9 — STEP 4: PERSIST TO DATABASE -->
# Step 4 — Save to SQL Server

**Two-step process** (chunk IDs are needed before storing vectors):

1. Insert the **Document** row → database generates its ID
2. Insert all **DocumentChunk** rows with content and page number  
   (Embedding column is left empty for now)

**Database schema:**

| Column | Type | Notes |
|---|---|---|
| Id | INT | Primary key, auto-generated |
| DocumentId | INT | Foreign key to Document |
| Content | NVARCHAR(MAX) | The chunk text |
| PageNumber | INT | Source page |
| Embedding | VECTOR(1536) | Filled in Step 5 |

---

<!-- SLIDE 10 — STEP 5: GENERATE EMBEDDINGS -->
# Step 5 — Generate & Store Embeddings

**Model:** text-embedding-3-small  
**Output:** 1536-dimensional float vector per chunk

**What happens:**
- Chunks are sent to OpenAI in **batches of 20** to respect rate limits
- Each chunk is converted to a vector (1536 numbers)
- Vectors are saved back into the Embedding column in SQL Server

**Why batches of 20?**

OpenAI has rate limits per minute.  
Batching reduces the number of API calls while staying within limits.

---

<!-- SLIDE 11 — WHAT IS AN EMBEDDING? -->
# What Is a Vector Embedding?

A vector embedding is a list of 1536 numbers that encodes the **meaning** of a text.

Texts with similar meaning produce vectors that are **mathematically close** to each other — regardless of the exact words used.

**Example:**

| Text | Vector position |
|---|---|
| "What is the revenue?" | [0.012, -0.847, 0.334, ...] |
| "Annual income figures" | [0.018, -0.841, 0.329, ...] ← very close! |
| "The cat sat on a mat" | [0.723, 0.102, -0.551, ...] ← very far |

> This is what makes **semantic search** possible — finding by meaning, not keywords.

---

<!-- SLIDE 12 — PHASE 2 OVERVIEW -->
# Phase 2 — Query Pipeline

> Runs **per user question**

```
 User question
      │
     [1] Convert question to a vector (same model as ingestion)
      │
     [2] Cosine similarity search → retrieve top 5 chunks
      │
     [3] Build a context block with numbered source references
      │
     [4] Construct System prompt + User message
      │
     [5] Send to GPT-4o-mini → get answer
      │
     [6] Return answer + source citations to the client
```

---

<!-- SLIDE 13 — STEP 6: EMBED THE QUESTION -->
# Step 6 — Embed the User's Question

The user's question is converted into a vector using **the exact same embedding model** that was used during ingestion.

**Why must it be the same model?**

Different models produce vectors in different mathematical spaces.  
Comparing vectors from two different models would give meaningless distances.

**Properties of this step:**
- Very fast — typically under 100 milliseconds
- Very cheap — a tiny fraction of a chat completion call
- A single API call regardless of how many documents are stored

---

<!-- SLIDE 14 — STEP 7: VECTOR SEARCH -->
# Step 7 — Cosine Similarity Search

The question vector is compared against all stored chunk vectors in SQL Server.

**SQL Server computes cosine distance natively** using the VECTOR_DISTANCE function.

**How cosine distance works:**

| Metric | Meaning |
|---|---|
| Distance = 0 | Vectors are identical (perfect match) |
| Distance = 1 | Vectors are completely unrelated |
| Relevance = 1 − Distance | Higher is better |

Results are **sorted by distance ascending** and the **top 5** most relevant chunks are returned.

---

<!-- SLIDE 15 — STEP 8: BUILD THE PROMPT -->
# Step 8 — Build the Grounding Prompt

The retrieved chunks are assembled into a numbered source list:

```
[Source 1: annual_report.pdf, Page 12]
Net revenue for fiscal year 2023 was $4.2 billion...

[Source 2: annual_report.pdf, Page 13]
Operating expenses were $1.8 billion...
```

**The system message instructs the model:**

> "Answer questions ONLY from the provided sources.  
> If the sources are insufficient, say so clearly."

**Why is this critical?**

Without this constraint, the LLM fills missing gaps with its training data — producing answers that sound correct but are not grounded in the document. This is called **hallucination**.

---

<!-- SLIDE 16 — STEP 9: GENERATE THE ANSWER -->
# Step 9 — Generate the Answer with the LLM

Two messages are sent to GPT-4o-mini:

1. **System message** — contains the grounding instruction and the source chunks
2. **User message** — contains the original question

**Model:** GPT-4o-mini (fast, cost-effective, sufficient for Q&A tasks)

**Abstraction benefit:**

The service uses `IChatClient` from `Microsoft.Extensions.AI`.  
This means the underlying model can be swapped — OpenAI, Azure OpenAI, Ollama — by changing a single registration line in Program.cs, with zero changes to service code.

---

<!-- SLIDE 17 — STEP 10: RETURN WITH CITATIONS -->
# Step 10 — Return Answer + Source Citations

**Response structure:**

```
{
  answer:  "Net revenue for FY2023 was $4.2 billion...",
  sources: [
    { documentName: "annual_report.pdf", pageNumber: 12, relevance: 0.93 },
    { documentName: "annual_report.pdf", pageNumber: 13, relevance: 0.88 }
  ]
}
```

**Why include sources?**

- Users can open the original document and verify the answer
- Relevance scores show how confident the retrieval was
- Verifiable answers build trust and reduce hallucination risk perception

---

<!-- SLIDE 18 — KEY DESIGN DECISIONS -->
# Key Design Decisions

| Decision | Why |
|---|---|
| SQL Server VECTOR type | No extra infrastructure needed. Scales to millions of chunks. |
| 200-char overlap | Prevents information loss at chunk boundaries |
| Same embedding model always | Cosine similarity only works within the same vector space |
| IChatClient / IEmbeddingGenerator | Swap providers by changing one registration line |
| Batch size 20 | Stays within OpenAI rate limits with minimal latency |
| System prompt constraint | Primary defence against hallucination |
| Source citations in response | Verifiable answers, increased user trust |

---

<!-- SLIDE 19 — COMPLETE FLOW -->
# Complete Data Flow

**Ingestion (once per document):**

```
PDF upload → validate → save to disk
         → extract text page by page
         → split into overlapping chunks (1000 / 200)
         → save Document + Chunks to SQL Server
         → generate embeddings in batches of 20
         → store vectors in Embedding column
```

**Query (per user request):**

```
User question
  → embed question (same model)
  → cosine search → top 5 chunks
  → assemble [Source N: file, Page X] context
  → System prompt + User message → GPT-4o-mini
  → return { answer, sources[] }
```

---

<!-- SLIDE 20 — SUMMARY -->
# Summary

**RAG = Retrieve first, then Generate**

| Phase | Steps | What happens |
|---|---|---|
| **Ingestion** | 1 – 5 | PDF → text → chunks → vectors → SQL Server |
| **Query** | 6 – 10 | Question → vector → cosine search → LLM → answer |

**The golden rule:**

> The embedding model is the **bridge** between ingestion and query.  
> Use the **exact same model** for both — always.

---

<!-- SLIDE 21 — TECH STACK -->
# Tech Stack Reference

| Layer | Technology |
|---|---|
| API | ASP.NET Core Minimal API (.NET 10) |
| Language Model | OpenAI GPT-4o-mini |
| Embedding Model | OpenAI text-embedding-3-small (1536 dimensions) |
| Vector Store | SQL Server — native VECTOR column type |
| PDF Parsing | PdfPig |
| AI Abstractions | Microsoft.Extensions.AI (IChatClient, IEmbeddingGenerator) |
| ORM | Entity Framework Core with VECTOR_DISTANCE support |

---

<!-- SLIDE 22 — CLOSING -->
# Thank You

**Repository:** Fcakiroglu16/MicrosoftAgentFrameworkPlayground

**Resources:**
- [Microsoft.Extensions.AI docs](https://learn.microsoft.com/en-us/dotnet/ai/ai-extensions)
- [SQL Server Vector Support](https://learn.microsoft.com/en-us/sql/relational-databases/vectors/vectors-sql-server)
- [OpenAI Embeddings Guide](https://platform.openai.com/docs/guides/embeddings)
- [PdfPig GitHub](https://github.com/UglyToad/PdfPig)

> *Built with .NET 10 · OpenAI · SQL Server · Microsoft.Extensions.AI · PdfPig*
