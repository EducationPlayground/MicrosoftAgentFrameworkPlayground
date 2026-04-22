using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.RazorPages.Pages;

public class ChatModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ChatModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty]
    public string Question { get; set; } = string.Empty;

    [BindProperty]
    public int? DocumentId { get; set; }

    [BindProperty]
    public Guid? SessionId { get; set; }

    public List<ChatMessageDto> Messages { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        var client = _httpClientFactory.CreateClient("ChatApi");
        var response = await client.PostAsJsonAsync("/api/chat/sessions", new { documentId = DocumentId });
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CreateSessionResponseDto>();
            SessionId = result?.SessionId;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Question))
        {
            ErrorMessage = "Please enter a question.";
            await LoadMessagesAsync();
            return Page();
        }

        if (SessionId is null)
        {
            ErrorMessage = "Session expired. Please refresh the page.";
            return Page();
        }

        var client = _httpClientFactory.CreateClient("ChatApi");

        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/chat/sessions/{SessionId}/messages",
                new { question = Question });

            if (!response.IsSuccessStatusCode)
                ErrorMessage = $"API error: {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Failed to connect to API: {ex.Message}";
        }

        Question = string.Empty;
        await LoadMessagesAsync();
        return Page();
    }

    private async Task LoadMessagesAsync()
    {
        if (SessionId is null) return;

        var client = _httpClientFactory.CreateClient("ChatApi");
        try
        {
            var response = await client.GetAsync($"/api/chat/sessions/{SessionId}/messages");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SessionMessagesResponseDto>();
                Messages = result?.Messages ?? new();
            }
        }
        catch (HttpRequestException) { /* silently ignore, Messages stays empty */ }
    }
}

public record CreateSessionResponseDto(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

public record SessionMessagesResponseDto(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("messages")] List<ChatMessageDto> Messages
);

public record ChatMessageDto(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("sources")] List<SourceReferenceDto>? Sources
);

public record AskQuestionResponseDto(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("sources")] List<SourceReferenceDto> Sources
);

public record SourceReferenceDto(
    [property: JsonPropertyName("documentName")] string DocumentName,
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("relevance")] double Relevance
);

