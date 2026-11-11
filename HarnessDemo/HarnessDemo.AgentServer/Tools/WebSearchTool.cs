using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Tools;

/// <summary>
/// İnternet araması. Harness'in varsayılan <see cref="HostedWebSearchTool"/>'u OpenAI'ın
/// <c>web_search_options</c> alanına çevrilir ve OpenRouter bunu araçlarla birlikte sunmaz.
/// Bu yüzden aramayı ayrı bir araç olarak veriyoruz: OpenRouter'ın web eklentisini
/// (<c>model:online</c>) kullanan ikinci bir istemciye soruyu sorar, cevabı ve kaynakları döner.
/// Arama, sohbette sıradan bir araç çağrısı olarak görünür.
/// </summary>
public sealed class WebSearchTool(IChatClient onlineClient, ILogger<WebSearchTool> logger)
{
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(SearchAsync, "web_search");

    [Description("İnternette güncel bilgi arar. Kısa bir cevap ve kaynak bağlantıları döner. Güncel olaylar, sürüm bilgileri ve doğrulanması gereken her bilgi için kullan.")]
    public async Task<string> SearchAsync(
        [Description("Arama sorgusu, örn. '.NET 10 son yama sürümü'.")] string query,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("🌐 web_search({Query})", query);

        var response = await onlineClient.GetResponseAsync(
            [
                new(ChatRole.System, "Web arama sonuçlarına dayanarak kısa ve olgusal cevap ver. Sonuçlarda olmayan bilgi ekleme."),
                new(ChatRole.User, query),
            ],
            cancellationToken: cancellationToken);

        var sources = response.Messages
            .SelectMany(m => m.Contents)
            .SelectMany(c => c.Annotations ?? [])
            .OfType<CitationAnnotation>()
            .Where(c => c.Url is not null)
            .Select(c => new { c.Title, url = c.Url!.ToString() })
            .DistinctBy(s => s.url)
            .Take(8)
            .ToList();

        return JsonSerializer.Serialize(new { query, answer = response.Text, sources });
    }
}
