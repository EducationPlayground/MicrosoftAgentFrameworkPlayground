using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Text.Json;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;
using WebApplication.API.Models;

namespace WebApplication.API.Services;

public class ChatHistoryService(AppDbContext db)
{
    public async Task<Guid> CreateSessionAsync(int? documentId)
    {
        var session = new ChatSession
        {
            DocumentId = documentId
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public async Task<List<ChatMessage>> GetHistoryAsync(Guid sessionId)
    {
        var records = await db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return records.Select(r => new ChatMessage(
            r.Role == "user" ? ChatRole.User : ChatRole.Assistant,
            r.Content
        )).ToList();
    }

    public async Task SaveTurnAsync(
        Guid sessionId,
        string question,
        string answer,
        List<SourceReference> sources)
    {
        var sourcesJson = sources.Count > 0
            ? JsonSerializer.Serialize(sources)
            : null;

        db.ChatMessages.Add(new ChatMessageRecord
        {
            ChatSessionId = sessionId,
            Role = "user",
            Content = question
        });

        db.ChatMessages.Add(new ChatMessageRecord
        {
            ChatSessionId = sessionId,
            Role = "assistant",
            Content = answer,
            SourcesJson = sourcesJson
        });

        await db.SaveChangesAsync();
    }

    public async Task<SessionMessagesResponse> GetMessagesAsync(Guid sessionId)
    {
        var records = await db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var messages = records.Select(r =>
        {
            List<SourceReference>? sources = null;
            if (r.SourcesJson is not null)
                sources = JsonSerializer.Deserialize<List<SourceReference>>(r.SourcesJson);

            return new ChatMessageDto(r.Role, r.Content, r.CreatedAt, sources);
        }).ToList();

        return new SessionMessagesResponse(sessionId, messages);
    }
}
