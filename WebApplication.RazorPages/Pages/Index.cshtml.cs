using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.RazorPages.Pages
{
    public class IndexModel : PageModel
    {
        private const string ConversationIdCookieName = "ConversationId";

        private readonly IHttpClientFactory _httpClientFactory;

        public IndexModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public List<ChatMessageDto> ChatHistory { get; set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            try
            {
                var conversationId = Request.Cookies[ConversationIdCookieName];
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    return;
                }

                var client = _httpClientFactory.CreateClient("ChatApi");
                var response = await client.GetAsync($"/chat/history?conversationId={Uri.EscapeDataString(conversationId)}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var history = await response.Content.ReadFromJsonAsync<List<ChatMessageDto>>(cancellationToken: cancellationToken);
                    if (history != null)
                    {
                        ChatHistory = history;
                    }
                }
            }
            catch
            {
                // Sessizce hatayı yutabiliriz ya da boş oluşturabiliriz.
            }
        }

        public async Task<IActionResult> OnPostChatAsync([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "message is required." });
            }

            var client = _httpClientFactory.CreateClient("ChatApi");

            var conversationId = Request.Cookies[ConversationIdCookieName];

            using var response = await client.PostAsJsonAsync("/chat", new ChatRequest(request.Message, conversationId), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                return StatusCode((int)response.StatusCode, new { error = errorText });
            }

            var chatResponse = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
            if (chatResponse is null)
            {
                return StatusCode(502, new { error = "Invalid response from chat API." });
            }

            // İlk mesajdan sonra dönen conversationId'yi tarayıcıya cookie olarak kaydediyoruz.
            Response.Cookies.Append(ConversationIdCookieName, chatResponse.ConversationId, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return new JsonResult(chatResponse);
        }

        public sealed record ChatRequest(string Message, string? ConversationId = null);
        public sealed record ChatResponse(string ConversationId, string Reply);
        public sealed record ChatMessageDto(string Role, string Content);
    }
}
