using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using WebApplication.API;
using WebApplication.API.Data;
using WebApplication.API.Providers;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// Database
builder.Services.AddDbContext<ChatHistoryDbContext>(options =>
    options.UseInMemoryDatabase("ChatHistoryDb"));


// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddChatClient(openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ConversationContext>();
builder.Services.AddSingleton<AIAgent>(sp =>
    sp.GetRequiredService<IChatClient>().AsAIAgent(new ChatClientAgentOptions
    {
        Name = "BasicLinearChat",
        ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful assistant. Keep replies short and clear."
        },
        // Singleton agent: provider, ConversationId'yi istek anında scoped
        // ConversationContext'ten kendi içinde çözümlüyor.
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
(ChatRequest request, AIAgent agent, ConversationContext conversationContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return TypedResults.BadRequest("message is required.");
    }

    // 1. Client bir conversationId gönderdiyse onu kullan, göndermediyse yeni bir tane oluştur.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString()
        : request.ConversationId;

    // 2. Singleton agent'ın ChatHistoryProvider'ı ConversationId'yi buradan okuyacak.
    conversationContext.ConversationId = conversationId;

    // 3. Her istekte yeni bir (boş) AgentSession oluştur; geçmiş mesajlar zaten
    // EfCoreChatHistoryProvider tarafından conversationId üzerinden DB'den yüklenecek.
    var session = await agent.CreateSessionAsync(cancellationToken);

    // 4. Agent'ı bu objeyle çalıştır
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    return TypedResults.Ok(new ChatResponse(conversationId, response.Text));
});


app.Run();

public sealed record ChatRequest(string Message, string? ConversationId = null);

public sealed record ChatResponse(string SessionId, string Reply);
