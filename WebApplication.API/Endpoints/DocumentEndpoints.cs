using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;
using WebApplication.API.Models;
using WebApplication.API.Services;

namespace WebApplication.API.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this global::Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        group.MapPost("/upload", UploadDocument).DisableAntiforgery();
        group.MapGet("/", GetAllDocuments);
        group.MapGet("/{id:int}", GetDocumentById);
        group.MapDelete("/{id:int}", DeleteDocument);
    }

    // -----------------------------------------------------------------------
    // Endpoint handlers
    // -----------------------------------------------------------------------

    private static async Task<IResult> UploadDocument(
        IFormFile file,
        AppDbContext db,
        PdfProcessingService pdfService,
        LlmChunkingService chunkingService,
        EmbeddingService embeddingService,
        VectorSearchService vectorSearchService,
        IWebHostEnvironment env,
        CancellationToken cancellationToken)
    {
        // Adım 0 – doğrulama
        var validationError = ValidateUploadedFile(file);
        if (validationError is not null)
            return validationError;

        // Adım 1 – dosyayı diske kaydet
        var filePath = await SaveFileToDiskAsync(file, env.WebRootPath, cancellationToken);

        // Adım 2 – PDF'den sayfa metinlerini çıkar, LLM ile semantik parçalara böl
        var (pageCount, chunks) = await ExtractAndChunkPagesAsync(filePath, pdfService, chunkingService, cancellationToken);
        if (chunks.Count == 0)
            return Results.BadRequest("PDF dosyasından metin çıkarılamadı.");

        // Adım 3 – Belge ve parçaları veritabanına kaydet
        var (document, chunkEntities) = await SaveDocumentAndChunksAsync(filePath, file.FileName, pageCount, chunks, db, cancellationToken);

        // Adım 4 – Her parça için embedding üret, vektör sütununu güncelle
        await IndexChunkEmbeddingsAsync(chunkEntities, embeddingService, vectorSearchService);

        return Results.Created($"/api/documents/{document.Id}", ToUploadResponse(document));
    }

    private static async Task<IResult> GetAllDocuments(AppDbContext db)
    {
        var documents = await db.Documents
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => ToDocumentDto(d))
            .ToListAsync();

        return Results.Ok(documents);
    }

    private static async Task<IResult> GetDocumentById(int id, AppDbContext db)
    {
        var document = await db.Documents.FindAsync(id);
        return document is null
            ? Results.NotFound()
            : Results.Ok(ToDocumentDto(document));
    }

    private static async Task<IResult> DeleteDocument(int id, AppDbContext db, IWebHostEnvironment env, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FindAsync([id], cancellationToken);
        if (document is null)
            return Results.NotFound();

        DeleteFileFromDisk(document.FileName, env.WebRootPath);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    // -----------------------------------------------------------------------
    // Adım 0 – Doğrulama
    // -----------------------------------------------------------------------

    private static IResult? ValidateUploadedFile(IFormFile file)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Sadece PDF dosyaları yüklenebilir.");

        return null;
    }

    // -----------------------------------------------------------------------
    // Adım 1 – Dosyayı diske kaydet
    // -----------------------------------------------------------------------

    private static async Task<string> SaveFileToDiskAsync(IFormFile file, string webRootPath, CancellationToken cancellationToken)
    {
        var uploadsDir = Path.Combine(webRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var savedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsDir, savedFileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);

        return filePath;
    }

    // -----------------------------------------------------------------------
    // Adım 2 – PDF'i oku → sayfaları çıkar → LLM ile parçala
    // -----------------------------------------------------------------------

    private static async Task<(int PageCount, List<(string Content, int PageNumber)> Chunks)> ExtractAndChunkPagesAsync(
        string filePath,
        PdfProcessingService pdfService,
        LlmChunkingService chunkingService,
        CancellationToken cancellationToken)
    {
        var (pageCount, pages) = ReadPdfPages(filePath, pdfService);

        var chunks = new List<(string Content, int PageNumber)>();
        foreach (var page in pages)
        {
            var pageChunks = await chunkingService.ChunkAsync(page.Content, cancellationToken);
            foreach (var chunkText in pageChunks)
                chunks.Add((chunkText, page.PageNumber));
        }

        return (pageCount, chunks);
    }

    private static (int PageCount, List<PdfPageText> Pages) ReadPdfPages(string filePath, PdfProcessingService pdfService)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return pdfService.ExtractPages(stream);
    }

    // -----------------------------------------------------------------------
    // Adım 3 – Belge ve parçaları veritabanına kaydet
    // -----------------------------------------------------------------------

    private static async Task<(Document Document, List<DocumentChunk> Chunks)> SaveDocumentAndChunksAsync(
        string filePath,
        string originalFileName,
        int pageCount,
        List<(string Content, int PageNumber)> chunks,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var document = await SaveDocumentAsync(filePath, originalFileName, pageCount, chunks.Count, db, cancellationToken);
        var chunkEntities = await SaveChunksAsync(document.Id, chunks, db, cancellationToken);

        return (document, chunkEntities);
    }

    private static async Task<Document> SaveDocumentAsync(
        string filePath, string originalFileName, int pageCount, int totalChunks,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var document = new Document
        {
            FileName = Path.GetFileName(filePath),
            OriginalFileName = originalFileName,
            UploadedAt = DateTime.UtcNow,
            PageCount = pageCount,
            TotalChunks = totalChunks
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    private static async Task<List<DocumentChunk>> SaveChunksAsync(
        int documentId, List<(string Content, int PageNumber)> chunks,
        AppDbContext db, CancellationToken cancellationToken)
    {
        var chunkEntities = chunks.Select((c, i) => new DocumentChunk
        {
            DocumentId = documentId,
            ChunkIndex = i,
            Content = c.Content,
            PageNumber = c.PageNumber
        }).ToList();

        db.DocumentChunks.AddRange(chunkEntities);
        await db.SaveChangesAsync(cancellationToken);

        return chunkEntities;
    }

    // -----------------------------------------------------------------------
    // Adım 4 – Embedding üret → vektör sütununa yaz
    // -----------------------------------------------------------------------

    private static async Task IndexChunkEmbeddingsAsync(
        List<DocumentChunk> chunkEntities,
        EmbeddingService embeddingService,
        VectorSearchService vectorSearchService)
    {
        var texts = chunkEntities.Select(c => c.Content).ToList();
        var embeddings = await embeddingService.GetEmbeddingsAsync(texts);

        for (var i = 0; i < chunkEntities.Count; i++)
            await vectorSearchService.SaveChunkWithEmbeddingAsync(chunkEntities[i].Id, embeddings[i]);
    }

    // -----------------------------------------------------------------------
    // Yardımcılar
    // -----------------------------------------------------------------------

    private static void DeleteFileFromDisk(string fileName, string webRootPath)
    {
        var filePath = Path.Combine(webRootPath, "uploads", fileName);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static UploadDocumentResponse ToUploadResponse(Document d) =>
        new(d.Id, d.OriginalFileName, d.PageCount, d.TotalChunks, d.UploadedAt);

    private static DocumentDto ToDocumentDto(Document d) =>
        new(d.Id, d.OriginalFileName, d.PageCount, d.TotalChunks, d.UploadedAt);
}
