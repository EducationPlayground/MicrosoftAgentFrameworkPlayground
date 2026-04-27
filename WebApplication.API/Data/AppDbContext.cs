using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();
}
