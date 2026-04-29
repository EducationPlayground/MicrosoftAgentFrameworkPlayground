using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this global::Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var group = app.MapGroup("/todos").WithTags("Todos");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var todos = await db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync();
            return Results.Ok(todos);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            return todo is null ? Results.NotFound() : Results.Ok(todo);
        });

        group.MapPost("/", async (Todo todo, AppDbContext db) =>
        {
            todo.CreatedAt = DateTime.UtcNow;
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return Results.Created($"/todos/{todo.Id}", todo);
        });

        group.MapPut("/{id:int}", async (int id, Todo updated, AppDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
                return Results.NotFound();

            todo.Title = updated.Title;
            todo.IsCompleted = updated.IsCompleted;
            await db.SaveChangesAsync();
            return Results.Ok(todo);
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
                return Results.NotFound();

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
