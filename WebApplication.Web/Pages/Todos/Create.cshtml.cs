using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Todos;

public class CreateModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var client = httpClientFactory.CreateClient("TodoApi");
        await client.PostAsJsonAsync("/todos", new { Title });
        return RedirectToPage("Index");
    }
}
