using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApplication.API.Data;

namespace WebApplication.API.Providers;

public sealed class EfCoreChatHistoryProvider : ChatHistoryProvider
{
    /// <summary>
    /// Tracks the per-session <see cref="State"/> (containing the conversation id),
    /// used to load and persist that session's state across invocations.
    /// </summary>
    private readonly ProviderSessionState<State> _sessionState;
    private readonly IServiceProvider _serviceProvider;

    public EfCoreChatHistoryProvider(
        IServiceProvider serviceProvider,
        string? stateKey = null)
    {
        _serviceProvider = serviceProvider;
        _sessionState = new ProviderSessionState<State>(
            _ => new State
            {
                ConversationId = ResolveConversationId() ?? Guid.NewGuid().ToString()
            },
            stateKey ?? this.GetType().Name);
    }

    /// <summary>
    /// Resolves the active conversation id from the current HTTP request's scoped
    /// <see cref="ConversationContext"/>, if available.
    /// </summary>
    private string? ResolveConversationId() =>
        _serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext?
            .RequestServices.GetService<ConversationContext>()?.ConversationId;


    /// <summary>
    /// Retrieves the chat history for the current session from the database, so it can be
    /// supplied to the agent before invocation.
    /// </summary>
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, 
        CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        
        // Scope to access EF Core DbContext
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ChatHistoryDbContext>();

        var dbState = await dbContext.ChatSessionStates.FindAsync([state.ConversationId], cancellationToken);
        if (dbState != null)
        {
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson);
            return messages ?? [];
        }

        return [];
    }

    /// <summary>
    /// Persists the new request/response messages produced during the invocation to the
    /// database, appending them to the session's existing chat history.
    /// </summary>
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, 
        CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ChatHistoryDbContext>();

        var dbState = await dbContext.ChatSessionStates.FindAsync([state.ConversationId], cancellationToken);

        List<ChatMessage> existingMessages = new();
        if (dbState != null)
        {
            existingMessages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson) ?? [];
        }
        else
        {
            dbState = new ChatSessionState { ConversationId = state.ConversationId };
            dbContext.ChatSessionStates.Add(dbState);
        }

        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []).ToList();
        existingMessages.AddRange(allNewMessages);

        dbState.MessagesJson = JsonSerializer.Serialize(existingMessages);
        await dbContext.SaveChangesAsync(cancellationToken);

        this._sessionState.SaveState(context.Session, state);
    }

    public sealed class State
    {
        public string ConversationId { get; set; } = string.Empty;
    }
}
