namespace WebApplication.API.Models;

public record CreateSessionRequest(int? DocumentId);

public record CreateSessionResponse(Guid SessionId, DateTime CreatedAt);

public record SendMessageRequest(string Question);

public record ChatMessageDto(
    string Role,
    string Content,
    DateTime CreatedAt,
    List<SourceReference>? Sources);

public record SessionMessagesResponse(
    Guid SessionId,
    List<ChatMessageDto> Messages);
