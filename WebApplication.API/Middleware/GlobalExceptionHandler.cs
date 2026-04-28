using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApplication.API.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.FindFirstValue("sub")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        var (statusCode, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad request"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (userId is not null)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
            {
                logger.LogError(
                    exception,
                    "Unhandled exception: {ExceptionType} — {Message}",
                    exception.GetType().Name,
                    exception.Message);
            }
        }
        else
        {
            logger.LogError(
                exception,
                "Unhandled exception: {ExceptionType} — {Message}",
                exception.GetType().Name,
                exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
        
            Status = statusCode,
            Title = title,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
