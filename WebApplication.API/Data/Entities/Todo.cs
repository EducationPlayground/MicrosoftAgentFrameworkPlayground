namespace WebApplication.API.Data.Entities;

public class Todo
{
    public int Id { get; set; }
    public string Title { get; set; } = default!;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
}
