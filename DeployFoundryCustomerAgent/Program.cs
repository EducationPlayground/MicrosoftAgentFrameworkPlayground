using Azure.AI.OpenAI;
using Azure.Identity;
using DeployFoundryCustomerAgent.Agent;
using DeployFoundryCustomerAgent.Protocol;
using DeployFoundryCustomerAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

// Register in-memory product data store and tools
builder.Services.AddSingleton<ProductDataStore>();
builder.Services.AddSingleton<CustomerAgentTools>();

// Register the ResponseHandler (Foundry Hosted Agent protocol)
builder.Services.AddResponsesServer<CustomerAgentHandler>();

// Register AIAgent — config env var'larından okunur (Foundry otomatik enjekte eder)
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var tools = sp.GetRequiredService<CustomerAgentTools>();

    // FOUNDRY_PROJECT_ENDPOINT ve MODEL_DEPLOYMENT_NAME Foundry tarafından otomatik enjekte edilir.
    // Lokal geliştirmede appsettings.json veya user-secrets üzerinden set edilebilir.
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    var endpoint = builder.Configuration["FOUNDRY_PROJECT_ENDPOINT"]!;
    logger.LogInformation("FOUNDRY_PROJECT_ENDPOINT: {Endpoint}", endpoint);

    var deploymentName = builder.Configuration["MODEL_DEPLOYMENT_NAME"]!;
    logger.LogInformation("MODEL_DEPLOYMENT_NAME: {DeploymentName}", deploymentName);

    // Lokal geliştirmede ApiKey varsa kullan; Foundry'de managed identity (DefaultAzureCredential) devreye girer.
    var apiKey = builder.Configuration["APIKEY"];
    logger.LogInformation("APIKEY configured: {HasApiKey}", !string.IsNullOrEmpty(apiKey));
    IChatClient chatClient = !string.IsNullOrEmpty(apiKey)
        ? new AzureOpenAIClient(
            new Uri(endpoint),
            new System.ClientModel.ApiKeyCredential(apiKey)
          ).GetChatClient(deploymentName).AsIChatClient()
        : new AzureOpenAIClient(
            new Uri(endpoint),
            new DefaultAzureCredential()
          ).GetChatClient(deploymentName).AsIChatClient();

    return chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Name = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "CustomerServiceAgent",
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

// AIAgent singleton'ını startup'ta resolve et — loglar uygulama ayağa kalktığında yazılsın


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Foundry Hosted Agent protocol endpoint'leri:
//   POST /responses  — sohbet, streaming, multi-turn
//   GET  /readiness  — platform health check
app.MapResponsesServer();
app.Run();

