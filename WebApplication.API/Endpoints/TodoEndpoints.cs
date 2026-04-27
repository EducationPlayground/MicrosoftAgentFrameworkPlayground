using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos").WithTags("Todos");

        group.MapGet("/", async (AppDbContext db) =>
            await db.Todos.ToListAsync());

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Todos.FindAsync(id) is { } todo
                ? Results.Ok(todo)
                : Results.NotFound());

        group.MapPost("/", async (CreateTodoRequest request, AppDbContext db) =>
        {
            var todo = new TodoItem
            {
                Title = request.Title,
                IsCompleted = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.Created($"/todos/{todo.Id}", todo);
        });

        group.MapPut("/{id:int}", async (int id, UpdateTodoRequest request, AppDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return Results.NotFound();

            todo.Title = request.Title;
            todo.IsCompleted = request.IsCompleted;
            await db.SaveChangesAsync();
            return Results.Ok(todo);
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return Results.NotFound();

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }
}

public record CreateTodoRequest(string Title);
public record UpdateTodoRequest(string Title, bool IsCompleted);
