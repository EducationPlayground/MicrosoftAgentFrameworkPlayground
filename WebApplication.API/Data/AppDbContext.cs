using Microsoft.EntityFrameworkCore;

namespace WebApplication.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ChatSessionState> ChatSessionStates { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            
            // Seed data
            entity.HasData(
                new Product { Id = 1, Name = "Laptop X", Description = "High performance laptop", Category = "Electronics", Price = 1500.00m, Stock = 50 },
                new Product { Id = 2, Name = "Smartphone Y", Description = "Latest smartphone with 5G", Category = "Electronics", Price = 999.99m, Stock = 100 },
                new Product { Id = 3, Name = "Coffee Maker", Description = "Automatic programmable coffee maker", Category = "Home", Price = 80.00m, Stock = 30 },
                new Product { Id = 4, Name = "Office Chair", Description = "Ergonomic mesh office chair", Category = "Home", Price = 120.50m, Stock = 20 },
                new Product { Id = 5, Name = "Running Shoes", Description = "Comfortable running shoes", Category = "Clothing", Price = 65.00m, Stock = 75 },
                new Product { Id = 6, Name = "T-Shirt", Description = "Cotton plain t-shirt", Category = "Clothing", Price = 15.00m, Stock = 200 },
                new Product { Id = 7, Name = "Science Fiction Book", Description = "Bestselling sci-fi novel", Category = "Books", Price = 12.99m, Stock = 150 },
                new Product { Id = 8, Name = "Headphones", Description = "Noise-cancelling over-ear headphones", Category = "Electronics", Price = 250.00m, Stock = 40 }
            );
        });
    }
}
