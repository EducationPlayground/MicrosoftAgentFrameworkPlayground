using System.Text;
using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class RagService(
    IChatClient chatClient,
    EmbeddingService embeddingService,
    VectorSearchService vectorSearchService)
{
    private const string SystemPrompt = """
        Sen bir kurumsal döküman asistanısın. Sana verilen kaynak içeriklerine dayanarak soruları yanıtla.
        Kurallar:
        - Sadece verilen kaynaklardan bilgi kullan
        - Eğer kaynaklar soruyu yanıtlamak için yeterli değilse, bunu belirt
        - Yanıtlarını Türkçe olarak ver
        - Kısa ve öz yanıtlar ver
        """;

    public async Task<(string Answer, List<ChunkSearchResult> Sources)> AskAsync(string question, int? documentId = null)
    {
        // Adım 1 – Soruyu vektöre dönüştür
        var questionEmbedding = await EmbedQuestionAsync(question);

        // Adım 2 – Vektör veritabanında benzer parçaları bul
        var relevantChunks = await RetrieveRelevantChunksAsync(questionEmbedding, documentId);
        if (relevantChunks.Count == 0)
            return ("İlgili döküman içeriği bulunamadı. Lütfen önce PDF dökümanlarını yükleyin.", []);

        // Adım 3 – Parçalardan LLM için bağlam metni oluştur
        var context = BuildContextFromChunks(relevantChunks);

        // Adım 4 – LLM ile yanıt üret
        var answer = await GenerateAnswerAsync(context, question);

        return (answer, relevantChunks);
    }

    // -----------------------------------------------------------------------
    // Adım 1 – Soruyu embedding vektörüne dönüştür
    // -----------------------------------------------------------------------

    private async Task<float[]> EmbedQuestionAsync(string question) =>
        await embeddingService.GetEmbeddingAsync(question);

    // -----------------------------------------------------------------------
    // Adım 2 – En yakın parçaları vektör veritabanından getir
    // -----------------------------------------------------------------------

    private async Task<List<ChunkSearchResult>> RetrieveRelevantChunksAsync(float[] questionEmbedding, int? documentId) =>
        await vectorSearchService.SearchSimilarChunksAsync(questionEmbedding, topN: 5, documentId);

    // -----------------------------------------------------------------------
    // Adım 3 – Chunk listesini LLM'e gönderilebilir kaynak metnine çevir
    // -----------------------------------------------------------------------

    private static string BuildContextFromChunks(List<ChunkSearchResult> chunks)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine($"[Kaynak {i + 1}: {chunk.DocumentName}, Sayfa {chunk.PageNumber}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Adım 4 – LLM'e bağlamı ve soruyu gönder, yanıt al
    // -----------------------------------------------------------------------

    private async Task<string> GenerateAnswerAsync(string context, string question)
    {
        var messages = BuildRagMessages(context, question);
        var response = await chatClient.GetResponseAsync(messages);
        return response.Text;
    }

    private static List<ChatMessage> BuildRagMessages(string context, string question) =>
    [
        new(ChatRole.System, SystemPrompt),
        new(ChatRole.User, $"Kaynaklar:\n{context}\nSoru: {question}")
    ];
}
