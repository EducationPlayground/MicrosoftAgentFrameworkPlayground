namespace WebApplication.API.Data.Entities;

public class ChatMessageRecord
{
    public int Id { get; set; }
    public Guid ChatSessionId { get; set; }
    public string Role { get; set; } = default!;
    public string Content { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SourcesJson { get; set; }

    public ChatSession ChatSession { get; set; } = default!;
}
