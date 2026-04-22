using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.RazorPages.Pages;

public class DocumentsModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DocumentsModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public List<DocumentDto> Documents { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDocumentsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (UploadFile is null || UploadFile.Length == 0)
        {
            ErrorMessage = "Please select a PDF file.";
            await LoadDocumentsAsync();
            return Page();
        }

        var client = _httpClientFactory.CreateClient("ChatApi");

        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = UploadFile.OpenReadStream();
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "file", UploadFile.FileName);

            var response = await client.PostAsync("/api/documents/upload", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
                SuccessMessage = $"'{result?.OriginalFileName}' uploaded successfully. Pages: {result?.PageCount}, Chunks: {result?.TotalChunks}.";
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                ErrorMessage = $"Upload failed: {(int)response.StatusCode} – {error}";
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Failed to connect to API: {ex.Message}";
        }

        await LoadDocumentsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var client = _httpClientFactory.CreateClient("ChatApi");

        try
        {
            var response = await client.DeleteAsync($"/api/documents/{id}");

            if (!response.IsSuccessStatusCode)
                ErrorMessage = $"Delete failed: {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Failed to connect to API: {ex.Message}";
        }

        await LoadDocumentsAsync();
        return Page();
    }

    private async Task LoadDocumentsAsync()
    {
        var client = _httpClientFactory.CreateClient("ChatApi");

        try
        {
            var docs = await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents");
            Documents = docs ?? new List<DocumentDto>();
        }
        catch (HttpRequestException)
        {
            Documents = new List<DocumentDto>();
        }
    }
}

public record DocumentDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("originalFileName")] string OriginalFileName,
    [property: JsonPropertyName("pageCount")] int PageCount,
    [property: JsonPropertyName("totalChunks")] int TotalChunks,
    [property: JsonPropertyName("uploadedAt")] DateTime UploadedAt
);

public record UploadDocumentResponseDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("originalFileName")] string OriginalFileName,
    [property: JsonPropertyName("pageCount")] int PageCount,
    [property: JsonPropertyName("totalChunks")] int TotalChunks,
    [property: JsonPropertyName("uploadedAt")] DateTime UploadedAt
);
