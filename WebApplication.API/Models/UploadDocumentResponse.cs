namespace WebApplication.API.Models;

public record UploadDocumentResponse(
    int Id,
    string FileName,
    int PageCount,
    int TotalChunks,
    DateTime UploadedAt);
