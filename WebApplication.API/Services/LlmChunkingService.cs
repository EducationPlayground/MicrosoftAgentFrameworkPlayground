using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class LlmChunkingService(
    IChatClient chatClient,
    ILogger<LlmChunkingService> logger)
{
    private const string SystemPrompt = """
        Sen bir döküman parçalama (chunking) asistanısın.
        Sana verilen metni, RAG (Retrieval Augmented Generation) için en uygun şekilde anlamlı parçalara böl.

        KURALLAR:
        - Her parça kendi başına anlamlı ve kendi kendine yeten bir birim olmalı (1-3 paragraf).
        - Metni AYNEN KORU. Özetleme, yeniden ifade etme, kelime ekleme/çıkarma YAPMA.
        - Doğal anlam sınırlarından böl (konu değişimi, başlık, bölüm geçişi).
        - Hedef parça boyutu: 400-1200 karakter. Çok kısa veya çok uzun parçalar üretme.
        - Parçalar orijinal metindeki sırayı korumalı.
        - Başlıkları bir sonraki içerikle aynı parçaya koy (yalnız başlık bırakma).
        - Tablolar ve listeler mümkünse bölünmemeli.

        ÇIKTI: Yalnızca JSON döndür. Şema:
        { "chunks": ["parça 1 metni", "parça 2 metni", ...] }
        """;

    public async Task<IReadOnlyList<string>> ChunkAsync(string pageText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageText))
            return [];

        try
        {
            var messages = BuildChunkingMessages(pageText);
            var response = await chatClient.GetResponseAsync<ChunkingResult>(messages, cancellationToken: cancellationToken);
            var chunks = ParseChunkingResponse(response);

            if (chunks is { Count: > 0 })
                return chunks;

            logger.LogWarning("LLM chunking boş sonuç döndürdü; ham sayfa metnine geri dönülüyor.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM chunking başarısız oldu; ham sayfa metnine geri dönülüyor.");
        }

        return [pageText];
    }

    // -----------------------------------------------------------------------
    // LLM'e gönderilecek mesaj listesini oluştur
    // -----------------------------------------------------------------------

    private static List<ChatMessage> BuildChunkingMessages(string pageText) =>
    [
        new(ChatRole.System, SystemPrompt),
        new(ChatRole.User, pageText)
    ];

    // -----------------------------------------------------------------------
    // LLM yanıtından chunk listesini çıkar ve temizle
    // -----------------------------------------------------------------------

    private static List<string>? ParseChunkingResponse(ChatResponse<ChunkingResult> response)
    {
        if (!response.TryGetResult(out var result) || result.Chunks is not { Count: > 0 } raw)
            return null;

        return raw
            .Select(c => c?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .ToList();
    }

    private sealed record ChunkingResult(List<string> Chunks);
}
