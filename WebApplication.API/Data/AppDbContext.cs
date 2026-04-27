using Microsoft.EntityFrameworkCore;

namespace WebApplication.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
