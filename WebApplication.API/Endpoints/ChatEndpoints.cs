using WebApplication.API.Models;
using WebApplication.API.Services;

namespace WebApplication.API.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this global::Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/ask", Ask);
    }

    private static async Task<IResult> Ask(AskQuestionRequest request, RagService ragService)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest("Soru boş olamaz.");

        var (answer, sources) = await ragService.AskAsync(request.Question, request.DocumentId);

        var sourceRefs = sources.Select(s => new SourceReference(
            s.DocumentName,
            s.PageNumber,
            Math.Round(s.Relevance, 4))).ToList();

        return Results.Ok(new AskQuestionResponse(answer, sourceRefs));
    }
}
