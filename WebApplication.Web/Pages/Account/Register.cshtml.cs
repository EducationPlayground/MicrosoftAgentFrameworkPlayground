using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Account;

public class RegisterModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var client = httpClientFactory.CreateClient("TodoApi");
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            Username = Input.Username,
            Email = Input.Email,
            Password = Input.Password
        });

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "Registration failed. The username or email may already be taken.");
            return Page();
        }

        return RedirectToPage("/Account/Login");
    }
}
