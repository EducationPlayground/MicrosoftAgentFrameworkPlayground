namespace WebApplication.API;

/// <summary>
/// Scoped (per-request) carrier for the active conversation id, used by the
/// singleton agent's chat history provider to key the persisted chat history.
/// </summary>
public sealed class ConversationContext
{
    public string? ConversationId { get; set; }
}
