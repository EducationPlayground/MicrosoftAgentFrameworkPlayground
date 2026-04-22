using WebApplication.API.Models;
using WebApplication.API.Services;

namespace WebApplication.API.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this global::Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/ask", Ask);
        group.MapPost("/sessions", CreateSession);
        group.MapPost("/sessions/{sessionId:guid}/messages", SendMessage);
        group.MapGet("/sessions/{sessionId:guid}/messages", GetMessages);
    }

    private static async Task<IResult> Ask(AskQuestionRequest request, RagService ragService)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest("Question cannot be empty.");

        var (answer, sources) = await ragService.AskAsync(request.Question, request.DocumentId);

        var sourceRefs = sources.Select(s => new SourceReference(
            s.DocumentName,
            s.PageNumber,
            Math.Round(s.Relevance, 4))).ToList();

        return Results.Ok(new AskQuestionResponse(answer, sourceRefs));
    }

    private static async Task<IResult> CreateSession(
        CreateSessionRequest request,
        ChatHistoryService chatHistoryService)
    {
        var sessionId = await chatHistoryService.CreateSessionAsync(request.DocumentId);
        return Results.Ok(new CreateSessionResponse(sessionId, DateTime.UtcNow));
    }

    private static async Task<IResult> SendMessage(
        Guid sessionId,
        SendMessageRequest request,
        ChatHistoryService chatHistoryService,
        RagService ragService)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest("Question cannot be empty.");

        var history = await chatHistoryService.GetHistoryAsync(sessionId);
        var (answer, sources) = await ragService.AskWithHistoryAsync(request.Question, history);

        var sourceRefs = sources.Select(s => new SourceReference(
            s.DocumentName,
            s.PageNumber,
            Math.Round(s.Relevance, 4))).ToList();

        await chatHistoryService.SaveTurnAsync(sessionId, request.Question, answer, sourceRefs);

        return Results.Ok(new AskQuestionResponse(answer, sourceRefs));
    }

    private static async Task<IResult> GetMessages(
        Guid sessionId,
        ChatHistoryService chatHistoryService)
    {
        var result = await chatHistoryService.GetMessagesAsync(sessionId);
        return Results.Ok(result);
    }
}

