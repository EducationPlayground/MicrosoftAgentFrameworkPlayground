using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Tickets;

[Authorize]
public class DetailModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public TicketItem? Ticket { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        Ticket = await client.GetFromJsonAsync<TicketItem>($"/tickets/{id}");
        if (Ticket is null) return NotFound();
        return Page();
    }
}
