using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.RazorPages.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public IndexModel(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public void OnGet()
        {

        }

        public async Task<IActionResult> OnPostChatAsync([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "sessionId and message are required." });
            }

            var client = _httpClientFactory.CreateClient("ChatApi");
            using var response = await client.PostAsJsonAsync("/chat", request, cancellationToken);

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

            return new JsonResult(chatResponse);
        }

        public sealed record ChatRequest(string SessionId, string Message);
        public sealed record ChatResponse(string SessionId, string Reply);
    }
}
