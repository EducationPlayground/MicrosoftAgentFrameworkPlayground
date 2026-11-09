using AGUI.Abstractions;
using System.Text.Json;
using AGUI.Server;
using HarnessDemo.AgentServer.Agents;
using HarnessDemo.AgentServer.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using System.ClientModel;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// --- Model: OpenRouter (OpenAI uyumlu Chat Completions) --------------------
var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException("OPENROUTER_API_KEY environment variable is not set.");
var model = builder.Configuration["OpenRouter:Model"] ?? "anthropic/claude-haiku-5.5";

IChatClient chatClient = new OpenAIClient(
        new ApiKeyCredential(openRouterKey),
        new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") })
    .GetChatClient(model)
    .AsIChatClient();

builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton<OrderTools>();
builder.Services.AddAGUIServer();

const string DomainInstructions = """
    Sipariş destek asistanısın. Türkçe yanıt ver.
    Sipariş bilgilerini her zaman araçlarla doğrula; araç sonucunda olmayan neden veya teslim tarihi uydurma.
    Kayıt bulunamazsa bunu açıkça söyle. Eksik bilgiyi belirt.
    Müşteriye e-posta göndermek için send_customer_email aracını kullan; bu araç kullanıcı onayı gerektirir.
    """;

// --- 1) Normal agent: model + talimat + araçlar + araç çağırma döngüsü ------
var plainAgent = builder.AddAIAgent("plain-agent", (sp, name) =>
{
    var tools = sp.GetRequiredService<OrderTools>();

    return sp.GetRequiredService<IChatClient>().AsAIAgent(new ChatClientAgentOptions
    {
        Name = name,
        ChatOptions = new ChatOptions
        {
            Instructions = DomainInstructions,
            Tools = [.. tools.CreateReadTools(), new ApprovalRequiredAIFunction(tools.CreateSendEmailTool())],
        },

        // Onay isteği geçmişe model çağrısı anında yazılsın; aksi halde AG-UI resume'u bir sonraki
        // istekte bekleyen isteği bulamıyor. Harness bunu varsayılan olarak açık getirir.
        RequirePerServiceCallChatHistoryPersistence = true,
    });
});

// --- 2) Harness agent: aynı model ve araçlar + hazır çalışma altyapısı -----
var harnessAgent = builder.AddAIAgent("harness-agent", (sp, name) =>
{
    var tools = sp.GetRequiredService<OrderTools>();

    // Todo ve mod sağlayıcılarını kendimiz oluşturuyoruz ki durumlarını UI'a yayınlayabilelim.
    var todoProvider = new TodoProvider(new TodoProviderOptions());
    var modeProvider = new AgentModeProvider(new AgentModeProviderOptions());

    AIAgent harness = sp.GetRequiredService<IChatClient>().AsHarnessAgent(new HarnessAgentOptions
    {
        Name = name,
        ChatOptions = new ChatOptions
        {
            Instructions = DomainInstructions,
            Tools = [.. tools.CreateReadTools(), new ApprovalRequiredAIFunction(tools.CreateSendEmailTool())],
        },

        // Planlama: todo listesi ve plan/execute modları (varsayılanları dışarıdan veriyoruz).
        DisableTodoProvider = true,
        DisableAgentModeProvider = true,
        AIContextProviders = [todoProvider, modeProvider],

#pragma warning disable MAAI001 // Experimental: dosya belleği
        // Bellek: çalışma notları diskte; sunum sırasında klasör açılıp gösterilebilir.
        FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(builder.Environment.ContentRootPath, "agent-memory")),
#pragma warning restore MAAI001

        // Bağlam yönetimi (compaction) bilinçli olarak kapalı: MaxContextWindowTokens verildiğinde
        // 1.28.0 sürümünde hosted session ile sonraki turların mesajları modele ulaşmıyor (experimental).

        // Sınırlı çalışma: tek istekte en fazla 40 araç çağırma turu.
        MaximumIterationsPerRequest = 40,

        // Hosted web search OpenAI'ın web_search_options'ına çevrilir; OpenRouter sağlayıcıları bunu araçlarla sunmuyor.
        DisableWebSearch = true,
    });

    return new HarnessStateAgent(harness, todoProvider, modeProvider);
});

// threadId ile turlar arasında server-side session korunur (todo/mod/geçmiş).
// Yerel demo: tek güvenilir kullanıcı varsayımıyla izolasyon kapalı. Production'da kalıcı store + izolasyon gerekir.
plainAgent.WithInMemorySessionStore(withIsolation: false);
harnessAgent.WithInMemorySessionStore(withIsolation: false);

var app = builder.Build();

app.MapDefaultEndpoints();

// Harness durum içeriği → AG-UI STATE_SNAPSHOT
var harnessStreamOptions = new AGUIStreamOptions()
    .MapContent(content => content is DataContent { MediaType: HarnessStateAgent.MediaType } state
        ? [new StateSnapshotEvent { Snapshot = JsonDocument.Parse(state.Data).RootElement.Clone() }]
        : []);

app.MapAGUIServer(plainAgent, "/agent");
app.MapAGUIServer(harnessAgent, "/harness").WithMetadata(harnessStreamOptions);

app.MapGet("/", () => "HarnessDemo AG-UI server: POST /agent, POST /harness");

app.Run();
