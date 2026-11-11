using System.ClientModel;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using HarnessDemo.AgentServer.Agents;
using HarnessDemo.AgentServer.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// --- Model: OpenRouter (OpenAI uyumlu Chat Completions) ---------------------
var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException("OPENROUTER_API_KEY environment variable is not set.");
var model = builder.Configuration["OpenRouter:Model"] ?? "anthropic/claude-haiku-5.5";
// Web araması: OpenRouter web eklentisi (":online" son eki)
var searchModel = builder.Configuration["OpenRouter:SearchModel"] ?? $"{model}:online";

var openRouter = new OpenAIClient(
    new ApiKeyCredential(openRouterKey),
    new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") });

// --- Servisler --------------------------------------------------------------
builder.Services.AddAGUIServer();
builder.Services.AddSingleton(openRouter.GetChatClient(model).AsIChatClient());
builder.Services.AddSingleton<OrderTools>();

// Full harness için: web araması ve Docker sandbox.
builder.Services.AddSingleton(sp => new WebSearchTool(
    openRouter.GetChatClient(searchModel).AsIChatClient(),
    sp.GetRequiredService<ILogger<WebSearchTool>>()));
builder.Services.AddSingleton(_ =>
    FullHarnessAgent.CreateSandbox(Path.Combine(builder.Environment.ContentRootPath, "agent-workspace")));

// --- Agent'lar: her biri Agents/ klasöründe ayrı dosyada --------------------
// 1) Normal agent  → Agents/PlainOrderAgent.cs
// 2) Harness agent → Agents/HarnessOrderAgent.cs (1 ile aynı talimat ve araçlar: Agents/OrderAgentCommon.cs)
// 3) Full harness  → Agents/FullHarnessAgent.cs (web araması, sandbox, dosya erişimi, skill, arka plan agent'ı)
var plainAgent = builder.AddAIAgent("plain-agent", PlainOrderAgent.Create);
var harnessAgent = builder.AddAIAgent("harness-agent", HarnessOrderAgent.Create);
var fullAgent = builder.AddAIAgent("full-harness-agent", FullHarnessAgent.Create);

// threadId ile turlar arasında server-side session korunur (todo/mod/geçmiş).
// Yerel demo: tek güvenilir kullanıcı varsayımıyla izolasyon kapalı. Production'da kalıcı store + izolasyon gerekir.
foreach (var agent in new[] { plainAgent, harnessAgent, fullAgent })
{
    agent.WithInMemorySessionStore(withIsolation: false);
}

var app = builder.Build();

app.MapDefaultEndpoints();

// --- Endpoint'ler -----------------------------------------------------------
// Harness durum içeriği → AG-UI STATE_SNAPSHOT
var harnessStreamOptions = new AGUIStreamOptions()
    .MapContent(content => content is DataContent { MediaType: HarnessStateAgent.MediaType } state
        ? [new StateSnapshotEvent { Snapshot = JsonDocument.Parse(state.Data).RootElement.Clone() }]
        : []);

app.MapAGUIServer(plainAgent, "/agent");
app.MapAGUIServer(harnessAgent, "/harness").WithMetadata(harnessStreamOptions);
app.MapAGUIServer(fullAgent, "/full").WithMetadata(harnessStreamOptions);

app.MapGet("/", () => "HarnessDemo AG-UI server: POST /agent, POST /harness, POST /full");

app.Run();
