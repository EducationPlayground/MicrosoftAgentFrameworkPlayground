using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using DeployFoundryCustomerAgent.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Caching.Memory;
using CreateResponseRequest = DeployFoundryCustomerAgent.Protocol.CreateResponseRequest;
using ResponseContext = DeployFoundryCustomerAgent.Protocol.ResponseContext;

namespace DeployFoundryCustomerAgent.Agent;

/// <summary>
/// Foundry Hosted Agent protocol handler'ı.
/// Makale mimarisindeki ResponseHandler karşılığı: her /responses isteğinde
/// CreateAsync çağrılır, MAF AIAgent çalıştırılır ve yanıt döndürülür.
/// Multi-turn sohbet: ConversationId / X-Session-Id header'ı ile oturum cache'lenir.
/// </summary>
public class CustomerAgentHandler(AIAgent agent, IMemoryCache cache) : ResponseHandler
{
    public override IAsyncEnumerable<ResponseStreamEvent> CreateAsync(CreateResponse request,
        Azure.AI.AgentServer.Responses.ResponseContext context, CancellationToken cancellationToken)
    {
        //var sessionId = Guid.NewGuid().ToString();

        //// Aynı sessionId ile gelen isteklerde mevcut MAF oturumunu yeniden kullan (multi-turn)
        //if (!cache.TryGetValue(sessionId, out AgentSession? session) || session is null)
        //{
        //    session = await agent.CreateSessionAsync(cancellationToken);
        //    cache.Set(sessionId, session, TimeSpan.FromMinutes(30));
        //}

        //var reply = await agent.RunAsync(
        //    request.Input,
        //    session,
        //    cancellationToken: cancellationToken);

        //return new ResponseResult(sessionId, reply.Text ?? string.Empty);
        //TEST: Basit echo yanıtı döndür
        return new TextResponse(context, request,
            createText: async ct =>
            {
                var input = await context.GetInputTextAsync(cancellationToken: ct);
                return $"Echo: {input}";
            });
    }
}