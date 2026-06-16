using Azure.AI.OpenAI;
using DeployFoundryCustomerAgent.Agent;
using DeployFoundryCustomerAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

// Register in-memory product data store and tools
builder.Services.AddSingleton<ProductDataStore>();
builder.Services.AddSingleton<CustomerAgentTools>();

// Register AIAgent using Azure AI Foundry (AzureOpenAI endpoint + API key)
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var tools = sp.GetRequiredService<CustomerAgentTools>();

    IChatClient chatClient = new AzureOpenAIClient(
        new Uri(config["Foundry:Endpoint"]!),
        new ApiKeyCredential(config["Foundry:ApiKey"]!)
    ).GetChatClient(config["Foundry:DeploymentName"]!).AsIChatClient();

    return chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "CustomerServiceAgent",
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions()),
        ChatOptions = new ChatOptions
        {
            Instructions = """
                Sen bir e-ticaret müşteri hizmetleri asistanısın. Müşterilerin ürün sorularını yanıtlamak,
                ürün aramalarına yardımcı olmak ve stok bilgisi vermek için tasarlandın.
                Kullanıcılara her zaman Türkçe yanıt ver.
                Fiyat bilgisi verirken TL cinsinden belirt.
                Stokta olmayan ürünler için özür dile ve alternatif öner.
                Yalnızca mağazamızdaki ürünler hakkında bilgi ver.
                """,
            Tools =
            [
                AIFunctionFactory.Create(tools.SearchProductsAsync),
                AIFunctionFactory.Create(tools.GetProductByIdAsync),
                AIFunctionFactory.Create(tools.GetProductsByCategoryAsync),
                AIFunctionFactory.Create(tools.GetProductsByPriceRangeAsync),
                AIFunctionFactory.Create(tools.GetOutOfStockProductsAsync),
                AIFunctionFactory.Create(tools.GetInStockProductsAsync),
                AIFunctionFactory.Create(tools.GetAllCategoriesAsync),
            ]
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// POST /chat  — send a message to the customer agent
app.MapPost("/chat", async (ChatRequest request, AIAgent agent, IMemoryCache cache, CancellationToken ct) =>
{
    var sessionId = request.SessionId ?? Guid.NewGuid().ToString();

    if (!cache.TryGetValue(sessionId, out AgentSession? session) || session is null)
    {
        session = await agent.CreateSessionAsync(ct);
        cache.Set(sessionId, session);
    }

    var reply = await agent.RunAsync(request.Message, session, cancellationToken: ct);

    return Results.Ok(new ChatResponse(sessionId, reply.Text));
});

// DELETE /chat/{sessionId}  — clear a session
app.MapDelete("/chat/{sessionId}", (string sessionId, IMemoryCache cache) =>
{
    cache.Remove(sessionId);
    return Results.NoContent();
});

app.Run();

record ChatRequest(string Message, string? SessionId);
record ChatResponse(string SessionId, string Reply);

