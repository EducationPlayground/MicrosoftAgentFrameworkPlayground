using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using System.Text.Json;
using WebApplication.API.Data;
using WebApplication.API.Providers;

namespace WebApplication.API.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat");

        group.MapGet("/history", async (AIAgent agent, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            var sessionJson = httpContext.Session.GetString("AgentSessionData");
            if (string.IsNullOrEmpty(sessionJson))
            {
                return Results.Ok(new List<ChatMessageDto>());
            }

            var jsonElement = JsonSerializer.Deserialize<JsonElement>(sessionJson);
            var session = await agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);

            var stateInitializer = (AgentSession? s) => new EfCoreChatHistoryProvider.State();
            var providerSessionState = new ProviderSessionState<EfCoreChatHistoryProvider.State>(stateInitializer, typeof(EfCoreChatHistoryProvider).Name);
            var state = providerSessionState.GetOrInitializeState(session);

            if (string.IsNullOrEmpty(state.DbKey))
            {
                return Results.Ok(new List<ChatMessageDto>());
            }

            using var scope = app.ServiceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbState = await dbContext.ChatSessionStates.FindAsync([state.DbKey], cancellationToken);
            if (dbState != null)
            {
                var chatMessages = JsonSerializer.Deserialize<List<JsonElement>>(dbState.MessagesJson);
                if (chatMessages != null)
                {
                    var result = new List<ChatMessageDto>();
                    foreach (var msg in chatMessages)
                    {
                        if (!msg.TryGetProperty("Role", out var roleProp)) continue;
                        var role = roleProp.GetString() ?? "";
                        if (!role.Equals("user", StringComparison.OrdinalIgnoreCase) && !role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (msg.TryGetProperty("Contents", out var contentsProp) && contentsProp.ValueKind == JsonValueKind.Array)
                        {
                            var contentText = "";
                            foreach (var contentItem in contentsProp.EnumerateArray())
                            {
                                if (contentItem.TryGetProperty("$type", out var typeProp) && 
                                    typeProp.GetString() == "text" && 
                                    contentItem.TryGetProperty("Text", out var textProp))
                                {
                                    contentText += textProp.GetString() ?? "";
                                }
                            }

                            if (!string.IsNullOrEmpty(contentText))
                            {
                                result.Add(new ChatMessageDto(role, contentText));
                            }
                        }
                    }
                    return Results.Ok(result);
                }
            }

            return Results.Ok(new List<ChatMessageDto>());
        });

        group.MapPost("", async Task<Results<Ok<ChatResponse>, BadRequest<string>>>
            (ChatRequest request, AIAgent agent, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return TypedResults.BadRequest("message is required.");
            }

            httpContext.Session.SetString("Init", "true");

            var sessionJson = httpContext.Session.GetString("AgentSessionData");
            AgentSession session;

            if (string.IsNullOrEmpty(sessionJson))
            {
                session = await agent.CreateSessionAsync(cancellationToken);
            }
            else
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(sessionJson);
                session = await agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);
            }

            var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

            var updatedSessionJsonElement = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            httpContext.Session.SetString("AgentSessionData", updatedSessionJsonElement.GetRawText());

            return TypedResults.Ok(new ChatResponse(httpContext.Session.Id, response.Text));
        });
    }
}

public sealed record ChatRequest(string Message);
public sealed record ChatResponse(string SessionId, string Reply);
public sealed record ChatMessageDto(string Role, string Content);
