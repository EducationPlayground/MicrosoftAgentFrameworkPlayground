using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApplication.API.Data;

namespace WebApplication.API.Providers;

public sealed class EfCoreChatHistoryProvider : ChatHistoryProvider
{
    private readonly ProviderSessionState<State> _sessionState;
    private readonly IServiceProvider _serviceProvider;

    public EfCoreChatHistoryProvider(
        IServiceProvider serviceProvider,
        Func<AgentSession?, State>? stateInitializer = null,
        string? stateKey = null)
    {
        _serviceProvider = serviceProvider;
        _sessionState = new ProviderSessionState<State>(
            stateInitializer ?? (_ => new State { DbKey = Guid.NewGuid().ToString() }),
            stateKey ?? this.GetType().Name);
    }

    public string StateKey => this._sessionState.StateKey;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, 
        CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        
        // Scope to access EF Core DbContext
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var dbState = await dbContext.ChatSessionStates.FindAsync([state.DbKey], cancellationToken);
        if (dbState != null)
        {
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson);
            return messages ?? [];
        }

        return [];
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, 
        CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var dbState = await dbContext.ChatSessionStates.FindAsync([state.DbKey], cancellationToken);
        
        List<ChatMessage> existingMessages = new();
        if (dbState != null)
        {
            existingMessages = JsonSerializer.Deserialize<List<ChatMessage>>(dbState.MessagesJson) ?? [];
        }
        else
        {
            dbState = new ChatSessionState { SessionId = state.DbKey };
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
        public string DbKey { get; set; } = string.Empty;
    }
}
