using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Todos;

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IndexModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public List<TodoItem> Todos { get; set; } = [];

    public async Task OnGetAsync()
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        Todos = await client.GetFromJsonAsync<List<TodoItem>>("/todos") ?? [];
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        var todo = await client.GetFromJsonAsync<TodoItem>($"/todos/{id}");
        if (todo is null) return NotFound();

        await client.PutAsJsonAsync($"/todos/{id}", new { todo.Title, IsCompleted = !todo.IsCompleted });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        await client.DeleteAsync($"/todos/{id}");
        return RedirectToPage();
    }
}
