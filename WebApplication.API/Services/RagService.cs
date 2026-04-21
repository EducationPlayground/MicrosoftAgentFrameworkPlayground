using System.Text;
using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class RagService(
    IChatClient chatClient,
    EmbeddingService embeddingService,
    VectorSearchService vectorSearchService)
{
    public async Task<(string Answer, List<ChunkSearchResult> Sources)> AskAsync(string question, int? documentId = null)
    {
        // 1. Embed the question
        var queryEmbedding = await embeddingService.GetEmbeddingAsync(question);

        // 2. Find relevant chunks
        var relevantChunks = await vectorSearchService.SearchSimilarChunksAsync(queryEmbedding, topN: 5, documentId);

        if (relevantChunks.Count == 0)
        {
            return ("İlgili döküman içeriği bulunamadı. Lütfen önce PDF dökümanlarını yükleyin.", []);
        }

        // 3. Build context from chunks
        var contextBuilder = new StringBuilder();
        for (var i = 0; i < relevantChunks.Count; i++)
        {
            var chunk = relevantChunks[i];
            contextBuilder.AppendLine($"[Kaynak {i + 1}: {chunk.DocumentName}, Sayfa {chunk.PageNumber}]");
            contextBuilder.AppendLine(chunk.Content);
            contextBuilder.AppendLine();
        }

        // 4. Generate answer using LLM
        var systemPrompt = """
            Sen bir kurumsal döküman asistanısın. Sana verilen kaynak içeriklerine dayanarak soruları yanıtla.
            Kurallar:
            - Sadece verilen kaynaklardan bilgi kullan
            - Eğer kaynaklar soruyu yanıtlamak için yeterli değilse, bunu belirt
            - Yanıtlarını Türkçe olarak ver
            - Kısa ve öz yanıtlar ver
            """;

        var userPrompt = $"""
            Kaynaklar:
            {contextBuilder}

            Soru: {question}
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var response = await chatClient.GetResponseAsync(messages);
        var answer = response.Text;

        return (answer, relevantChunks);
    }
}
