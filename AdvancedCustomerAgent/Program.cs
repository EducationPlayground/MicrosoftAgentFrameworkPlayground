
using AdvancedCustomerAgent;
using AdvancedCustomerAgent.Services;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;

var builder = AgentHost.CreateBuilder(args);


var projectEndpoint = new Uri("https://education-test-resource.services.ai.azure.com");
var deployment = "gpt-5-mini";

var tools = new CustomerAgentTools(new ProductDataStore());
AIAgent agent = new AIProjectClient(projectEndpoint, new DefaultAzureCredential())
    .AsAIAgent(
        model: deployment,
        instructions: """"
                      Sen bir e-ticaret müşteri hizmetleri asistanısın. Müşterilerin ürün sorularını yanıtlamak,
                      ürün aramalarına yardımcı olmak ve stok bilgisi vermek için tasarlandın.
                      Kullanıcılara her zaman Türkçe yanıt ver.
                      Fiyat bilgisi verirken TL cinsinden belirt.
                      Stokta olmayan ürünler için özür dile ve alternatif öner.
                      Yalnızca mağazamızdaki ürünler hakkında bilgi ver.

                      """",
        name: "custer-agent", tools:
        [
            AIFunctionFactory.Create(tools.SearchProductsAsync),
            AIFunctionFactory.Create(tools.GetProductByIdAsync),
            AIFunctionFactory.Create(tools.GetProductsByCategoryAsync),
            AIFunctionFactory.Create(tools.GetProductsByPriceRangeAsync),
            AIFunctionFactory.Create(tools.GetOutOfStockProductsAsync),
            AIFunctionFactory.Create(tools.GetInStockProductsAsync),
            AIFunctionFactory.Create(tools.GetAllCategoriesAsync),
        ]);

//IChatClient chatClient = !string.IsNullOrEmpty(apiKey)
//    ? new AzureOpenAIClient(
//        new Uri(endpoint),
//        new System.ClientModel.ApiKeyCredential(apiKey)
//    ).GetChatClient(deploymentName).AsIChatClient()
//    : new AzureOpenAIClient(
//        new Uri(endpoint),
//        new DefaultAzureCredential()
//    ).GetChatClient(deploymentName).AsIChatClient();


//var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
//{
//    Name = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "CustomerServiceAgent",
//    ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions()),
//    ChatOptions = new ChatOptions
//    {
//        Instructions = """
//                       Sen bir e-ticaret müşteri hizmetleri asistanısın. Müşterilerin ürün sorularını yanıtlamak,
//                       ürün aramalarına yardımcı olmak ve stok bilgisi vermek için tasarlandın.
//                       Kullanıcılara her zaman Türkçe yanıt ver.
//                       Fiyat bilgisi verirken TL cinsinden belirt.
//                       Stokta olmayan ürünler için özür dile ve alternatif öner.
//                       Yalnızca mağazamızdaki ürünler hakkında bilgi ver.
//                       """,
//        Tools =
//        [
//            AIFunctionFactory.Create(tools.SearchProductsAsync),
//            AIFunctionFactory.Create(tools.GetProductByIdAsync),
//            AIFunctionFactory.Create(tools.GetProductsByCategoryAsync),
//            AIFunctionFactory.Create(tools.GetProductsByPriceRangeAsync),
//            AIFunctionFactory.Create(tools.GetOutOfStockProductsAsync),
//            AIFunctionFactory.Create(tools.GetInStockProductsAsync),
//            AIFunctionFactory.Create(tools.GetAllCategoriesAsync),
//        ]
//    }
//});

builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());
var app = builder.Build();


app.Run();