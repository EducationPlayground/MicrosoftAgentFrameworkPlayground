using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// Conversation session'ları istekler arasında bellekte tutmak için.
builder.Services.AddMemoryCache();

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
            ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions()
            {
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
                ChatReducer = new MessageCountingChatReducer(5)
#pragma warning restore MEAI001
            })
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
    (ChatRequest request, AIAgent agent, IMemoryCache cache, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return TypedResults.BadRequest("message is required.");
    }

    // 1. Conversation id'yi client bize gönderdiyse onu kullanıyoruz, gönderilmediyse yeni bir tane üretiyoruz.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("N")
        : request.ConversationId;

    // 2. Daha önce bu conversation id için kaydettiğimiz session'ı cache'den okuyoruz.
    if (!cache.TryGetValue<AgentSession>(conversationId, out var session) || session is null)
    {
        // 3. İlk defa geliyorsa yeni session oluşturuyoruz
        session = await agent.CreateSessionAsync(cancellationToken);
    }

    // 4. Agent'ı bu objeyle çalıştır
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    // 5. Session'ı (chat history dahil) tekrar cache'e yaz
    cache.Set(conversationId, session, TimeSpan.FromMinutes(30));

    return TypedResults.Ok(new ChatResponse(conversationId, response.Text));
});

// Belirli bir conversation'ın chat history'sini döndürür.
app.MapGet("/chat/{conversationId}/history", Results<Ok<IReadOnlyList<ChatMessageDto>>, NotFound<string>>
    (string conversationId, AIAgent agent, IMemoryCache cache) =>
{
    // 1. Session cache'de yoksa bu conversation için geçmiş de yok demektir.
    if (!cache.TryGetValue<AgentSession>(conversationId, out var session) || session is null)
    {
        return TypedResults.NotFound($"No conversation found for id '{conversationId}'.");
    }

    // 2. Agent'a bağlı InMemoryChatHistoryProvider üzerinden session'daki mesajları oku.
    var provider = agent.GetService<InMemoryChatHistoryProvider>();
    var messages = provider?.GetMessages(session) ?? [];

    // 3. Sadece rol + metin bilgisini dışarıya aç.
    var history = messages
        .Select(m => new ChatMessageDto(m.Role.Value, m.Text))
        .ToArray();

    return TypedResults.Ok<IReadOnlyList<ChatMessageDto>>(history);
});


app.Run();

public sealed record ChatRequest(string Message, string? ConversationId = null);

public sealed record ChatResponse(string ConversationId, string Reply);

public sealed record ChatMessageDto(string Role, string Text);
