using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Agents;

/// <summary>
/// Normal agent: model + talimat + araçlar + araç çağırma döngüsü. Başka bir şey yok.
/// </summary>
public static class PlainOrderAgent
{
    public static AIAgent Create(IServiceProvider sp, string name) =>
        sp.GetRequiredService<IChatClient>().AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = OrderAgentCommon.CreateChatOptions(sp),

            // Onay isteği geçmişe model çağrısı anında yazılsın; aksi halde AG-UI resume'u bir sonraki
            // istekte bekleyen isteği bulamıyor. Harness bunu varsayılan olarak açık getirir.
            RequirePerServiceCallChatHistoryPersistence = true,
        });
}
