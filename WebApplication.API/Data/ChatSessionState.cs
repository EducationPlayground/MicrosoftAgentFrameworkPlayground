using System.ComponentModel.DataAnnotations;

namespace WebApplication.API.Data;

public class ChatSessionState
{
    [Key]
    public string ConversationId { get; set; } = string.Empty;
    public string MessagesJson { get; set; } = "[]";
}
