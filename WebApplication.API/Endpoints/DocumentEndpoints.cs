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

    private static async Task<IResult> UploadDocument(
        IFormFile file,
        AppDbContext db,
        PdfProcessingService pdfService,
        EmbeddingService embeddingService,
        VectorSearchService vectorSearchService,
        IWebHostEnvironment env)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Sadece PDF dosyaları yüklenebilir.");

        // Save file to disk
        var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var savedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsDir, savedFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Extract text chunks from PDF
        List<(string Content, int PageNumber)> chunks;
        int pageCount;

        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            (pageCount, chunks) = pdfService.ExtractChunks(stream);
        }

        if (chunks.Count == 0)
            return Results.BadRequest("PDF dosyasından metin çıkarılamadı.");

        // Save document entity
        var document = new Document
        {
            FileName = savedFileName,
            OriginalFileName = file.FileName,
            UploadedAt = DateTime.UtcNow,
            PageCount = pageCount,
            TotalChunks = chunks.Count
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        // Save chunks to database
        var chunkEntities = chunks.Select((c, i) => new DocumentChunk
        {
            DocumentId = document.Id,
            ChunkIndex = i,
            Content = c.Content,
            PageNumber = c.PageNumber
        }).ToList();

        db.DocumentChunks.AddRange(chunkEntities);
        await db.SaveChangesAsync();

        // Generate embeddings and save vectors
        var texts = chunkEntities.Select(c => c.Content).ToList();
        var embeddings = await embeddingService.GetEmbeddingsAsync(texts);

        for (var i = 0; i < chunkEntities.Count; i++)
        {
            await vectorSearchService.SaveChunkWithEmbeddingAsync(chunkEntities[i].Id, embeddings[i]);
        }

        return Results.Created($"/api/documents/{document.Id}",
            new UploadDocumentResponse(
                document.Id,
                document.OriginalFileName,
                document.PageCount,
                document.TotalChunks,
                document.UploadedAt));
    }

    private static async Task<IResult> GetAllDocuments(AppDbContext db)
    {
        var documents = await db.Documents
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentDto(
                d.Id,
                d.OriginalFileName,
                d.PageCount,
                d.TotalChunks,
                d.UploadedAt))
            .ToListAsync();

        return Results.Ok(documents);
    }

    private static async Task<IResult> GetDocumentById(int id, AppDbContext db)
    {
        var document = await db.Documents.FindAsync(id);
        if (document is null)
            return Results.NotFound();

        return Results.Ok(new DocumentDto(
            document.Id,
            document.OriginalFileName,
            document.PageCount,
            document.TotalChunks,
            document.UploadedAt));
    }

    private static async Task<IResult> DeleteDocument(int id, AppDbContext db, IWebHostEnvironment env)
    {
        var document = await db.Documents.FindAsync(id);
        if (document is null)
            return Results.NotFound();

        // Delete file from disk
        var filePath = Path.Combine(env.ContentRootPath, "Uploads", document.FileName);
        if (File.Exists(filePath))
            File.Delete(filePath);

        db.Documents.Remove(document);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}
