using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using WebApplication.API.Data;
using WebApplication.API.Providers;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

builder.Services.AddDistributedMemoryCache();

// Database
builder.Services.AddDbContext<ChatHistoryDbContext>(options =>
    options.UseInMemoryDatabase("ChatHistoryDb"));


// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddChatClient(openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());
builder.Services.AddSingleton<AIAgent>(sp =>
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
            ChatHistoryProvider = new EfCoreChatHistoryProvider(sp)
        }));
var app = builder.Build();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
app.MapPost("/chat", async Task<Results<Ok<ChatResponse>, BadRequest<string>>>
    (ChatRequest request, AIAgent agent, Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return TypedResults.BadRequest("message is required.");
    }

    // 1. Client bir conversationId gönderdiyse onu kullan, göndermediyse yeni bir tane oluştur.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString()
        : request.ConversationId;
    var cacheKey = $"AgentSessionData:{conversationId}";

    // 2. Bu conversationId'ye ait daha önce kaydedilmiş agent session verisini cache'den okuyoruz.
    var sessionJson = await cache.GetStringAsync(cacheKey, cancellationToken);
    AgentSession session;

    if (string.IsNullOrEmpty(sessionJson))
    {
        // 3. İlk defa geliyorsa yeni session oluşturuyoruz.
        session = await agent.CreateSessionAsync(cancellationToken);
    }
    else
    {
        // 4. Daha önceden gelmişse, onu JSON'dan tekrar yüklüyoruz.
        var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(sessionJson);
        session = await agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);
    }

    // 5. Agent'ı bu objeyle çalıştır
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    // 6. Güncellenmiş session durumunu (chat history dahil) tekrar cache'e conversationId ile yaz.
    var updatedSessionJsonElement = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
    await cache.SetStringAsync(
        cacheKey,
        updatedSessionJsonElement.GetRawText(),
        new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        },
        cancellationToken);

    return TypedResults.Ok(new ChatResponse(conversationId, response.Text));
});


app.Run();

public sealed record ChatRequest(string Message, string? ConversationId = null);

public sealed record ChatResponse(string SessionId, string Reply);
