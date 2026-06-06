using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos").WithTags("Todos").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, ILogger<Program> logger) =>
        {
            logger.LogInformation("GetAlL endpoint is working");

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            return await db.Todos
                .Where(t => t.UserId == userId)
                .ToListAsync();
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            return await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId) is { } todo
                ? Results.Ok(todo)
                : Results.NotFound();
        });

        group.MapPost("/", async (CreateTodoRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            if (userId is null) return Results.Unauthorized();

            var todo = new TodoItem
            {
                Title = request.Title,
                IsCompleted = false,
                CreatedAt = DateTime.UtcNow,
                UserId = userId
            };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.Created($"/todos/{todo.Id}", todo);
        });

        group.MapPut("/{id:int}", async (int id, UpdateTodoRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (todo is null) return Results.NotFound();

            todo.Title = request.Title;
            todo.IsCompleted = request.IsCompleted;
            await db.SaveChangesAsync();
            return Results.Ok(todo);
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db, ILogger<Program> logger) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");

            if (id <= 0)
            {
                logger.LogWarning("Invalid todo delete request with id {TodoId} for user {UserId}", id, userId ?? "unknown");
                return Results.BadRequest("Todo id must be greater than zero.");
            }

            try
            {
                var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
                if (todo is null) return Results.NotFound();

                db.Todos.Remove(todo);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
            // Ticket #2 reported DivideByZeroException from this route; keep endpoint resilient and log input values.
            catch (DivideByZeroException ex)
            {
                logger.LogError(ex, "Divide by zero while deleting todo {TodoId} for user {UserId}", id, userId ?? "unknown");
                return Results.Problem("Unable to delete todo item due to an internal numeric error.");
            }
        });

        return app;
    }
}

public record CreateTodoRequest(string Title);
public record UpdateTodoRequest(string Title, bool IsCompleted);
