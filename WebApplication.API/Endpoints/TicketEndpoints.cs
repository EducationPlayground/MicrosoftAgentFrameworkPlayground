using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets").WithTags("Tickets").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            var tickets = await db.Tickets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TicketDto(t.Id, t.Title, t.Description, t.Priority, t.Status, t.CreatedAt, t.UpdatedAt))
                .ToListAsync();
            return Results.Ok(tickets);
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            return ticket is not null
                ? Results.Ok(new TicketDto(ticket.Id, ticket.Title, ticket.Description, ticket.Priority, ticket.Status, ticket.CreatedAt, ticket.UpdatedAt))
                : Results.NotFound();
        });

        group.MapPost("/", async (CreateTicketRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            if (userId is null) return Results.Unauthorized();

            var ticket = new Ticket
            {
                Title = request.Title,
                Description = request.Description,
                Priority = request.Priority,
                Status = TicketStatus.Open,
                CreatedAt = DateTime.UtcNow,
                UserId = userId
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            return Results.Created($"/tickets/{ticket.Id}",
                new TicketDto(ticket.Id, ticket.Title, ticket.Description, ticket.Priority, ticket.Status, ticket.CreatedAt, ticket.UpdatedAt));
        });

        group.MapPut("/{id:int}", async (int id, UpdateTicketRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (ticket is null) return Results.NotFound();

            ticket.Title = request.Title;
            ticket.Description = request.Description;
            ticket.Priority = request.Priority;
            ticket.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new TicketDto(ticket.Id, ticket.Title, ticket.Description, ticket.Priority, ticket.Status, ticket.CreatedAt, ticket.UpdatedAt));
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? user.FindFirstValue("sub");
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (ticket is null) return Results.NotFound();

            db.Tickets.Remove(ticket);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }
}

public record CreateTicketRequest(string Title, string Description, TicketPriority Priority);
public record UpdateTicketRequest(string Title, string Description, TicketPriority Priority);
public record TicketDto(int Id, string Title, string Description, TicketPriority Priority, TicketStatus Status, DateTime CreatedAt, DateTime? UpdatedAt);
