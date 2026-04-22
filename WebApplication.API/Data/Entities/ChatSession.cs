namespace WebApplication.API.Data.Entities;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? DocumentId { get; set; }

    public List<ChatMessageRecord> Messages { get; set; } = [];
}
