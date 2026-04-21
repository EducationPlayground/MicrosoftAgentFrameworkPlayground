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

    public string? Answer { get; set; }

    public List<SourceReferenceDto> Sources { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Question))
        {
            ErrorMessage = "Lütfen bir soru girin.";
            return Page();
        }

        var client = _httpClientFactory.CreateClient("ChatApi");

        var requestBody = new { question = Question, documentId = DocumentId };

        try
        {
            var response = await client.PostAsJsonAsync("/api/chat/ask", requestBody);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AskQuestionResponseDto>();
                Answer = result?.Answer;
                Sources = result?.Sources ?? new List<SourceReferenceDto>();
            }
            else
            {
                ErrorMessage = $"API hatası: {(int)response.StatusCode} {response.ReasonPhrase}";
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"API'ye bağlanılamadı: {ex.Message}";
        }

        return Page();
    }
}

public record AskQuestionResponseDto(
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("sources")] List<SourceReferenceDto> Sources
);

public record SourceReferenceDto(
    [property: JsonPropertyName("documentName")] string DocumentName,
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("relevance")] double Relevance
);
