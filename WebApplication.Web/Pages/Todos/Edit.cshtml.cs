using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Todos;

[Authorize]
public class EditModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public bool IsCompleted { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var client = httpClientFactory.CreateClient("TodoApi");
        var todo = await client.GetFromJsonAsync<TodoItem>($"/todos/{Id}");
        if (todo is null) return NotFound();

        Title = todo.Title;
        IsCompleted = todo.IsCompleted;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var client = httpClientFactory.CreateClient("TodoApi");
        await client.PutAsJsonAsync($"/todos/{Id}", new { Title, IsCompleted });
        return RedirectToPage("Index");
    }
}
