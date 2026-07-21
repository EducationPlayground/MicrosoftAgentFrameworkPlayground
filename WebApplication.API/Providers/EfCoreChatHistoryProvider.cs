using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApplication.API.Data;

namespace WebApplication.API.Providers;

public sealed class EfCoreChatHistoryProvider : ChatHistoryProvider
{
    /// <summary>
    /// Tracks the per-session <see cref="State"/> (containing the database session id),
    /// used to load and persist that session's state across invocations.
    /// </summary>
    private readonly ProviderSessionState<State> _sessionState;
    private readonly IServiceProvider _serviceProvider;

    public EfCoreChatHistoryProvider(
        IServiceProvider serviceProvider,
        Func<AgentSession?, State>? stateInitializer = null,
        string? stateKey = null)
    {
        _serviceProvider = serviceProvider;
        _sessionState = new ProviderSessionState<State>(
            stateInitializer ?? (_ => new State { SessionId = Guid.NewGuid().ToString() }),
            stateKey ?? this.GetType().Name);
    }

    public string StateKey => this._sessionState.StateKey;

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
        
        var dbState = await dbContext.ChatSessionStates.FindAsync([state.SessionId], cancellationToken);
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
        
        var dbState = await dbContext.ChatSessionStates.FindAsync([state.SessionId], cancellationToken);

        List<ChatMessage> existingMessages = new();
        if (dbState != null)
        {
            existingMessages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson) ?? [];
        }
        else
        {
            dbState = new ChatSessionState { SessionId = state.SessionId };
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
        public string SessionId { get; set; } = string.Empty;
    }
}
