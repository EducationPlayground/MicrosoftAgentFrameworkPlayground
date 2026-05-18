using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Database


// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddChatClient(openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());
builder.Services.AddSingleton<AIAgent>(_ =>
    openAiClient
        .GetChatClient("gpt-4o-mini")
        .AsIChatClient()
        .AsAIAgent(new ChatClientAgentOptions
        {
            Name = "BasicLinearChat",
            ChatOptions = new ChatOptions
            {
                Instructions = "You are a helpful assistant. Keep replies short and clear."
            },
            ChatHistoryProvider = new InMemoryChatHistoryProvider()
        }));
var app = builder.Build();

app.UseSession();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
app.MapPost("/chat", async Task<Results<Ok<ChatResponse>, BadRequest<string>>>
    (ChatRequest request, AIAgent agent, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return TypedResults.BadRequest("message is required.");
    }

    // Session'ın başlatılmasını (cookie set edilmesini) garantilemek için geçici bir değer atıyoruz.
    httpContext.Session.SetString("Init", "true");

    // 1. Session id'yi client'tan değil, API tarafındaki entegre sunucu session mekanizmasından alıyoruz.
    // 1. Client'ın önceki isteğinde kaydettiğimiz session verisini ASP.NET Session'dan okuyoruz
    var sessionJson = httpContext.Session.GetString("AgentSessionData");
    AgentSession session;

    if (string.IsNullOrEmpty(sessionJson))
    {
        // 2. İlk defa geliyorsa yeni session oluşturuyoruz
        session = await agent.CreateSessionAsync(cancellationToken);
    }
    else
    {
        // 3. Daha önceden gelmişse, onu JSON'dan tekrar yüklüyoruz.
        var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(sessionJson);
        session = await agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);
    }

    // 4. Agent'ı bu objeyle çalıştır
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    // 5. Güncellenmiş session durumunu (chat history dahil) tekrar ASP.NET Session'a yaz
    var updatedSessionJsonElement = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
    httpContext.Session.SetString("AgentSessionData", updatedSessionJsonElement.GetRawText());

    return TypedResults.Ok(new ChatResponse(httpContext.Session.Id, response.Text));
});


app.Run();

public sealed record ChatRequest(string Message);

public sealed record ChatResponse(string SessionId, string Reply);
