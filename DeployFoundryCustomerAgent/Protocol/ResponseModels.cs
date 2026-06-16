namespace DeployFoundryCustomerAgent.Protocol;

/// <summary>
/// POST /responses isteğinin gövdesi.
/// Foundry gateway'in gönderdiği format: { "input": "...", "stream": false, "store": true }
/// </summary>
public record CreateResponseRequest(
    string Input,
    bool Stream = false,
    bool Store = true,
    string? ConversationId = null);

/// <summary>
/// Handler'dan dönen yanıt.
/// </summary>
public record ResponseResult(string SessionId, string Output);

/// <summary>
/// Her istek için platform tarafından sağlanan bağlam.
/// Foundry'nin enjekte ettiği oturum/kimlik bilgilerini taşır.
/// </summary>
public class ResponseContext
{
    public string? SessionId { get; init; }
    public string? AgentName { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
}
