using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Tickets;

public class TicketItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[Authorize]
public class IndexModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public List<TicketItem> Tickets { get; set; } = [];

    public async Task OnGetAsync()
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        Tickets = await client.GetFromJsonAsync<List<TicketItem>>("/tickets") ?? [];
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        await client.DeleteAsync($"/tickets/{id}");
        return RedirectToPage();
    }
}
