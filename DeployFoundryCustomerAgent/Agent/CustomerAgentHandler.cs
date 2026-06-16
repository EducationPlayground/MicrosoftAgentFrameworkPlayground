using DeployFoundryCustomerAgent.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Caching.Memory;

namespace DeployFoundryCustomerAgent.Agent;

/// <summary>
/// Foundry Hosted Agent protocol handler'ı.
/// Makale mimarisindeki ResponseHandler karşılığı: her /responses isteğinde
/// CreateAsync çağrılır, MAF AIAgent çalıştırılır ve yanıt döndürülür.
/// Multi-turn sohbet: ConversationId / X-Session-Id header'ı ile oturum cache'lenir.
/// </summary>
public class CustomerAgentHandler(AIAgent agent, IMemoryCache cache) : ResponseHandler
{
    public override async Task<ResponseResult> CreateAsync(
        CreateResponseRequest request,
        ResponseContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = context.SessionId ?? Guid.NewGuid().ToString();

        // Aynı sessionId ile gelen isteklerde mevcut MAF oturumunu yeniden kullan (multi-turn)
        if (!cache.TryGetValue(sessionId, out AgentSession? session) || session is null)
        {
            session = await agent.CreateSessionAsync(cancellationToken);
            cache.Set(sessionId, session, TimeSpan.FromMinutes(30));
        }

        var reply = await agent.RunAsync(
            request.Input,
            session,
            cancellationToken: cancellationToken);

        return new ResponseResult(sessionId, reply.Text ?? string.Empty);
    }
}
