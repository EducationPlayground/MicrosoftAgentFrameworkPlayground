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
WebApplication.API.Endpoints.ChatEndpoints.MapChatEndpoints(app);

app.Run();
