using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<AppUser> userManager) =>
        {
            var user = new AppUser { UserName = request.Username, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return Results.BadRequest(result.Errors);

            return Results.Created($"/auth/users/{user.Id}", new { user.Id, user.UserName, user.Email });
        });

        group.MapPost("/signin", async (
            SignInRequest request,
            UserManager<AppUser> userManager,
            IConfiguration configuration) =>
        {
            var user = await userManager.FindByNameAsync(request.Username);
            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            var token = GenerateJwtToken(user, configuration);
            return Results.Ok(new SignInResponse(token));
        });

        group.MapPost("/signout", () => Results.Ok())
            .RequireAuthorization();

        return app;
    }

    private static string GenerateJwtToken(AppUser user, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(jwtSection["ExpiryMinutes"]!)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record SignInRequest(string Username, string Password);
public record RegisterRequest(string Username, string Email, string Password);
public record SignInResponse(string Token);
