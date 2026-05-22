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

        public List<ChatMessageDto> ChatHistory { get; set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ChatApi");

                // Cookie'leri aktarıyoruz ki API bizim tarayıcı session id'mizle eşleşsin
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, "/chat/history");
                if (Request.Headers.TryGetValue("Cookie", out var cookieHeader))
                {
                    requestMessage.Headers.Add("Cookie", cookieHeader.ToString());
                }

                var response = await client.SendAsync(requestMessage, cancellationToken);
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

            // Cookie'leri aktarıyoruz ki API bizim tarayıcı session id'mizle eşleşsin
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/chat")
            {
                Content = JsonContent.Create(new { Message = request.Message })
            };
            if (Request.Headers.TryGetValue("Cookie", out var cookieHeader))
            {
                requestMessage.Headers.Add("Cookie", cookieHeader.ToString());
            }

            using var response = await client.SendAsync(requestMessage, cancellationToken);

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

            // API tarafında oluşturulan session cookie'sini (eğer ilk defa set edildiyse) tarayıcıya geri set etmeliyiz
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookie in setCookies)
                {
                    Response.Headers.Append("Set-Cookie", cookie);
                }
            }

            return new JsonResult(chatResponse);
        }

        public sealed record ChatRequest(string Message);
        public sealed record ChatResponse(string SessionId, string Reply);
        public sealed record ChatMessageDto(string Role, string Content);
    }
}
