using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using System.Text.Json;
using WebApplication.API.Data;
using WebApplication.API.Services;

namespace WebApplication.API.Endpoints;

public static class ChatEndpoints
{
    private const string Instructions =
        "Sen bir e-ticaret müşteri hizmetleri asistanısın. Ürünler hakkındaki soruları cevaplamak için verilen tool'ları kullan. Bilmediğin bilgileri uydurma; tool sonuçlarına dayan. Kısa, kibar ve Türkçe yanıt ver.";

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat");

        group.MapGet("/history", async (IServiceProvider serviceProvider, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await httpContext.Session.LoadAsync(cancellationToken);
            var sessionId = httpContext.Session.Id;

            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbState = await dbContext.ChatSessionStates.FindAsync([sessionId], cancellationToken);
            if (dbState is null)
            {
                return Results.Ok(new List<ChatMessageDto>());
            }

            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson, AIJsonUtilities.DefaultOptions) ?? [];
            var result = messages
                .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && !string.IsNullOrEmpty(m.Text))
                .Select(m => new ChatMessageDto(m.Role.Value, m.Text))
                .ToList();

            return Results.Ok(result);
        });

        group.MapPost("", async Task<Results<Ok<ChatResponse>, BadRequest<string>>>
            (ChatRequest request, IChatClient chatClient, ProductTools productTools, IServiceProvider serviceProvider, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return TypedResults.BadRequest("message is required.");
            }

            // Force session cookie creation so subsequent requests (and the Razor Pages client) align to the same session.
            httpContext.Session.SetString("Init", "true");
            var sessionId = httpContext.Session.Id;

            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var dbState = await dbContext.ChatSessionStates.FindAsync([sessionId], cancellationToken);
            List<ChatMessage> history = dbState is not null
                ? JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson, AIJsonUtilities.DefaultOptions) ?? []
                : [];

            history.Add(new ChatMessage(ChatRole.User, request.Message));

            var chatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = productTools.Tools
            };

            var response = await chatClient.GetResponseAsync(history, chatOptions, cancellationToken);

            history.AddMessages(response);

            if (dbState is null)
            {
                dbState = new ChatSessionState { SessionId = sessionId };
                dbContext.ChatSessionStates.Add(dbState);
            }

            dbState.MessagesJson = JsonSerializer.Serialize(history, AIJsonUtilities.DefaultOptions);
            await dbContext.SaveChangesAsync(cancellationToken);

            return TypedResults.Ok(new ChatResponse(sessionId, response.Text));
        });
    }
}

public sealed record ChatRequest(string Message);
public sealed record ChatResponse(string SessionId, string Reply);
public sealed record ChatMessageDto(string Role, string Content);
