using System.ComponentModel.DataAnnotations;

namespace WebApplication.API.Data;

public class ChatSessionState
{
    [Key]
    public string SessionId { get; set; } = string.Empty;
    public string MessagesJson { get; set; } = "[]";
}
