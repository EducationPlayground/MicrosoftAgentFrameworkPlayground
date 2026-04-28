namespace WebApplication.API.Data.Entities;

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}

public class Ticket
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketPriority Priority { get; set; } = TicketPriority.Low;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
}
