namespace WebApplication.API.Data.Entities;

public class Document
{
    public int Id { get; set; }
    public string FileName { get; set; } = default!;
    public string OriginalFileName { get; set; } = default!;
    public DateTime UploadedAt { get; set; }
    public int PageCount { get; set; }
    public int TotalChunks { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = [];
}
