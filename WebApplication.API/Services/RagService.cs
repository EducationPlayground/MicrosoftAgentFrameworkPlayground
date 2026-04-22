using System.Text;
using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class RagService(
    IChatClient chatClient,
    EmbeddingService embeddingService,
    VectorSearchService vectorSearchService)
{
    private const string SystemPrompt = """
        You are a corporate document assistant. Answer questions based on the source content provided to you.
        Rules:
        - Use only information from the provided sources
        - If the sources are insufficient to answer the question, state this clearly
        - Keep answers concise and focused
        - Provide accurate citations from the source documents
        """;

    public async Task<(string Answer, List<ChunkSearchResult> Sources)> AskAsync(string question, int? documentId = null)
    {
        // Step 1 – Convert question to embedding vector
        var questionEmbedding = await EmbedQuestionAsync(question);

        // Step 2 – Find similar chunks from vector database
        var relevantChunks = await RetrieveRelevantChunksAsync(questionEmbedding, documentId);
        if (relevantChunks.Count == 0)
            return ("No relevant document content found. Please upload PDF documents first.", []);

        // Step 3 – Build context text from chunks for LLM
        var context = BuildContextFromChunks(relevantChunks);

        // Step 4 – Generate answer with LLM
        var answer = await GenerateAnswerAsync(context, question);

        return (answer, relevantChunks);
    }

    // -----------------------------------------------------------------------
    // Step 1 – Convert question to embedding vector
    // -----------------------------------------------------------------------

    private async Task<float[]> EmbedQuestionAsync(string question) =>
        await embeddingService.GetEmbeddingAsync(question);

    // -----------------------------------------------------------------------
    // Step 2 – Retrieve nearest chunks from vector database
    // -----------------------------------------------------------------------

    private async Task<List<ChunkSearchResult>> RetrieveRelevantChunksAsync(float[] questionEmbedding, int? documentId) =>
        await vectorSearchService.SearchSimilarChunksAsync(questionEmbedding, topN: 5, documentId);

    // -----------------------------------------------------------------------
    // Step 3 – Convert chunk list to source text for LLM
    // -----------------------------------------------------------------------

    private static string BuildContextFromChunks(List<ChunkSearchResult> chunks)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine($"[Source {i + 1}: {chunk.DocumentName}, Page {chunk.PageNumber}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Step 4 – Send context and question to LLM, retrieve answer
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
        new(ChatRole.User, $"Sources:\n{context}\nQuestion: {question}")
    ];
}
