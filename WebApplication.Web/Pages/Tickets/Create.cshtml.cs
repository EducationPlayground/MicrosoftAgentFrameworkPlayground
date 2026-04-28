using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebApplication.Web.Pages.Tickets;

[Authorize]
public class CreateModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Description is required.")]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public int Priority { get; set; } = 0;

    public List<SelectListItem> PriorityOptions { get; } =
    [
        new SelectListItem("Low", "0"),
        new SelectListItem("Medium", "1"),
        new SelectListItem("High", "2"),
        new SelectListItem("Critical", "3"),
    ];

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var client = httpClientFactory.CreateClient("TodoApi");
        await client.PostAsJsonAsync("/tickets", new { Title, Description, Priority });
        return RedirectToPage("Index");
    }
}
