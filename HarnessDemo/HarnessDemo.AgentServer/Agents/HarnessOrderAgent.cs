using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Agents;

/// <summary>
/// Harness agent: <see cref="PlainOrderAgent"/> ile aynı model, talimat ve araçlar;
/// üstüne todo, plan/execute modu, dosya belleği, onay kuralları ve telemetri.
/// </summary>
public static class HarnessOrderAgent
{
    public static AIAgent Create(IServiceProvider sp, string name)
    {
        var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;

        // Todo ve mod sağlayıcılarını kendimiz oluşturuyoruz ki durumlarını UI'a yayınlayabilelim.
        var todoProvider = new TodoProvider(new TodoProviderOptions());
        var modeProvider = new AgentModeProvider(new AgentModeProviderOptions());

        AIAgent harness = sp.GetRequiredService<IChatClient>().AsHarnessAgent(new HarnessAgentOptions
        {
            Name = name,
            ChatOptions = OrderAgentCommon.CreateChatOptions(sp),

            // Planlama: todo listesi ve plan/execute modları (varsayılanları dışarıdan veriyoruz).
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            AIContextProviders = [todoProvider, modeProvider],

#pragma warning disable MAAI001 // Experimental: dosya belleği
            // Bellek: çalışma notları diskte; sunum sırasında klasör açılıp gösterilebilir.
            FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(contentRoot, "agent-memory")),
#pragma warning restore MAAI001

            // Bağlam yönetimi (compaction) bilinçli olarak kapalı: MaxContextWindowTokens verildiğinde
            // 1.28.0 sürümünde hosted session ile sonraki turların mesajları modele ulaşmıyor (experimental).

            // Sınırlı çalışma: MaximumIterationsPerRequest verilmedi; FunctionInvokingChatClient varsayılanı (40)
            // iki agent'ta da aynı. Sınır harness'e özgü değil, function invocation katmanından gelir.

            // Hosted web search OpenAI'ın web_search_options'ına çevrilir; OpenRouter sağlayıcıları bunu araçlarla sunmuyor.
            DisableWebSearch = true,
        });

        return new HarnessStateAgent(harness, todoProvider, modeProvider);
    }
}
