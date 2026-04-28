using System.Security.Claims;

namespace WebApplication.API.Middleware;

public class UserIdLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ILogger<UserIdLoggingMiddleware> logger)
    {
        var userId = context.User.FindFirstValue("sub")
                     ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is not null)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
            {
                await next(context);
            }
        }
        else
        {
            await next(context);
        }
    }
}
