using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApplication.Web.Pages.Account;

public class LoginModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var client = httpClientFactory.CreateClient("TodoApi");
        var response = await client.PostAsJsonAsync("/auth/signin", new
        {
            Username = Input.Username,
            Password = Input.Password
        });

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        var result = await response.Content.ReadFromJsonAsync<SignInResponse>();
        if (result?.Token is null)
        {
            ModelState.AddModelError(string.Empty, "Authentication failed.");
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Input.Username),
            new("jwt_token", result.Token)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return RedirectToPage("/Todos/Index");
    }

    private record SignInResponse(string Token);
}
