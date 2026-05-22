using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;
using WebApplication.API.Providers;
using System.Text.Json;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<WebApplication.API.Services.ProductTools>();

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
                Instructions = "Sen bir e-ticaret müşteri hizmetleri asistanısın. Ürünler hakkındaki soruları cevaplamak için verilen tool'ları kullan. Bilmediğin bilgileri uydurma; tool sonuçlarına dayan. Kısa, kibar ve Türkçe yanıt ver.",
                Tools = [ 
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().SearchProductsAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetProductByIdAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetProductsByCategoryAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetProductsByPriceRangeAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetOutOftStockProductsAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetInStockProductsAsync),
                    AIFunctionFactory.Create(sp.GetRequiredService<WebApplication.API.Services.ProductTools>().GetAllCategoriesAsync)
                ]
            },
            ChatHistoryProvider = new EfCoreChatHistoryProvider(sp)
        }));
var app = builder.Build();

// Migrate DB on startup
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSession();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
app.MapGet("/chat/history", async (AIAgent agent, HttpContext httpContext, CancellationToken cancellationToken) =>
{
    var sessionJson = httpContext.Session.GetString("AgentSessionData");
    if (string.IsNullOrEmpty(sessionJson))
    {
        return Results.Ok(new List<ChatMessageDto>());
    }

    var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(sessionJson);
    var session = await agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);

    // EfCoreChatHistoryProvider içindeki sessionState alanına erişmek için bir provider örneği yapıyoruz
    var stateInitializer = (AgentSession? s) => new EfCoreChatHistoryProvider.State();
    var providerSessionState = new ProviderSessionState<EfCoreChatHistoryProvider.State>(stateInitializer, typeof(EfCoreChatHistoryProvider).Name);
    var state = providerSessionState.GetOrInitializeState(session);

    if (state == null || string.IsNullOrEmpty(state.DbKey))
    {
        return Results.Ok(new List<ChatMessageDto>());
    }

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var dbState = await dbContext.ChatSessionStates.FindAsync([state.DbKey], cancellationToken);
    if (dbState != null)
    {
        // DB içindeki ChatMessage listesini deserialize edip rolü user veya assistant olanları alıyoruz.
        // ChatMessage'ların rollerini ve yazılarını dto ya mapliyoruz.
        var chatMessages = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(dbState.MessagesJson);
        if (chatMessages != null)
        {
            var result = new List<ChatMessageDto>();
            foreach (var msg in chatMessages)
            {
                // Sadece "user" ve "assistant" rollerini alıyoruz, tool vb. dışındakileri eliyoruz.
                if (!msg.TryGetProperty("Role", out var roleProp)) continue;
                var role = roleProp.GetString() ?? "";
                if (!role.Equals("user", StringComparison.OrdinalIgnoreCase) && !role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Sadece text içeren gerçek mesajları almak için Contents dizisindeki text tipindeki elemanları birleştiriyoruz.
                if (msg.TryGetProperty("Contents", out var contentsProp) && contentsProp.ValueKind == JsonValueKind.Array)
                {
                    var contentText = "";
                    foreach (var contentItem in contentsProp.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("$type", out var typeProp) && 
                            typeProp.GetString() == "text" && 
                            contentItem.TryGetProperty("Text", out var textProp))
                        {
                            contentText += textProp.GetString() ?? "";
                        }
                    }

                    if (!string.IsNullOrEmpty(contentText))
                    {
                        result.Add(new ChatMessageDto(role, contentText));
                    }
                }
            }
            return Results.Ok(result);
        }
    }

    return Results.Ok(new List<ChatMessageDto>());
});

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

public sealed record ChatMessageDto(string Role, string Content);
