namespace WebApplication.API.Models;

public record DocumentDto(
    int Id,
    string OriginalFileName,
    int PageCount,
    int TotalChunks,
    DateTime UploadedAt);
