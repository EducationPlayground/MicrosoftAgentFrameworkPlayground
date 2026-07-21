using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using OpenAI.Responses;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// Database


// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

// Service-managed storage:
// Sohbet geçmişi bizim tarafımızda (in-memory) değil, OpenAI Responses servisinde tutulur.
// AgentSession sadece servis tarafındaki conversation id'yi taşır; her RunAsync çağrısında
// önceki mesajları biz göndermeyiz, servis geçmişi kendisi yönetir.
builder.Services.AddSingleton<AIAgent>(_ =>
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
    openAiClient
        .GetResponsesClient()
        .AsAIAgent(
            model: "gpt-4o-mini",
            instructions: "You are a helpful assistant. Keep replies short and clear.",
            name: "ServiceManagedChat"));
#pragma warning restore OPENAI001

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
    (ChatRequest request, AIAgent agent, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return TypedResults.BadRequest("message is required.");
    }

    // Service-managed storage: sohbet geçmişi OpenAI Responses servisinde tutulur.
    // Bizim tarafımızda hiçbir şey saklamıyoruz; sadece servisin ürettiği conversation id'yi
    // client'a döndürüp bir sonraki istekte geri almamız yeterli.
    var chatAgent = (ChatClientAgent)agent;

    // 1. Client daha önce bir conversation id aldıysa o görüşmeyi kaldığı yerden devam ettiririz,
    //    yoksa yeni bir session (yeni bir servis conversation'ı) başlatırız.
    var session = string.IsNullOrWhiteSpace(request.ConversationId)
        ? await chatAgent.CreateSessionAsync(cancellationToken)
        : await chatAgent.CreateSessionAsync(request.ConversationId);

    // 2. Agent'ı çalıştır. Önceki mesajları biz göndermeyiz; servis geçmişi kendisi yönetir.
    var response = await chatAgent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    // 3. Servisin bu görüşme için kullandığı conversation id'yi client'a döndür.
    //    Client bir sonraki istekte bu id'yi göndererek aynı görüşmeye devam eder.

    var conversationId = (session as ChatClientAgentSession)?.ConversationId ?? string.Empty;

    return TypedResults.Ok(new ChatResponse(conversationId, response.Text));
});


app.Run();

public sealed record ChatRequest(string Message, string? ConversationId = null);

public sealed record ChatResponse(string ConversationId, string Reply);
