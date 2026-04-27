using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(500);
            entity.Property(e => e.Embedding).HasColumnType("vector(1536)");
        });
    }
}
