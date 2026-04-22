using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class LlmChunkingService(
    IChatClient chatClient,
    ILogger<LlmChunkingService> logger)
{
    private const string SystemPrompt = """
        You are a document chunking assistant.
        Split the given text into meaningful chunks optimized for RAG (Retrieval Augmented Generation).

        RULES:
        - Each chunk should be a self-contained, meaningful unit (1-3 paragraphs).
        - PRESERVE the text exactly. Do NOT summarize, rephrase, or add/remove words.
        - Split at natural semantic boundaries (topic changes, headings, section transitions).
        - Target chunk size: 400-1200 characters. Avoid chunks that are too short or too long.
        - Chunks must maintain the original text order.
        - Keep headings with the following content in the same chunk (don't leave headings alone).
        - Tables and lists should not be split if possible.

        OUTPUT: Return only JSON. Schema:
        { "chunks": ["chunk 1 text", "chunk 2 text", ...] }
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

            logger.LogWarning("LLM chunking returned empty result; falling back to raw page text.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM chunking failed; falling back to raw page text.");
        }

        return [pageText];
    }

    // -----------------------------------------------------------------------
    // Build message list for LLM
    // -----------------------------------------------------------------------

    private static List<ChatMessage> BuildChunkingMessages(string pageText) =>
    [
        new(ChatRole.System, SystemPrompt),
        new(ChatRole.User, pageText)
    ];

    // -----------------------------------------------------------------------
    // Extract chunk list from LLM response and clean it
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
