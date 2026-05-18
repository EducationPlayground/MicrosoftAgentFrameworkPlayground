using Microsoft.EntityFrameworkCore;

namespace WebApplication.API.Data;

public class ChatHistoryDbContext : DbContext
{
    public ChatHistoryDbContext(DbContextOptions<ChatHistoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<ChatSessionState> ChatSessionStates { get; set; } = null!;
}
