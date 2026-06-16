using Microsoft.AspNetCore.Mvc;

namespace DeployFoundryCustomerAgent.Protocol;

/// <summary>
/// Foundry Hosted Agent "Responses" protokolü için DI ve routing extension'ları.
/// Makale mimarisindeki AddResponsesServer() / MapResponsesServer() karşılığı.
/// Azure.AI.AgentServer.Responses paketi public olduğunda bu dosya kaldırılacak.
/// </summary>
public static class ResponsesServerExtensions
{
    /// <summary>
    /// Handler'ı DI container'a kaydeder.
    /// Program.cs: builder.Services.AddResponsesServer&lt;THandler&gt;()
    /// </summary>
    public static IServiceCollection AddResponsesServer<THandler>(
        this IServiceCollection services)
        where THandler : ResponseHandler
    {
        services.AddScoped<ResponseHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Foundry protokol endpoint'lerini açar:
    ///   POST /responses  — sohbet, streaming, multi-turn
    ///   GET  /readiness  — platform health check (200 OK yeterli)
    /// Program.cs: app.MapResponsesServer()
    /// </summary>
    public static IEndpointRouteBuilder MapResponsesServer(
        this IEndpointRouteBuilder app)
    {
        // GET /readiness — Foundry container'ın hazır olup olmadığını kontrol eder
        app.MapGet("/readiness", () => Results.Ok(new { status = "ready" }))
           .WithName("Readiness");

        // POST /responses — Foundry gateway'den gelen sohbet istekleri
        app.MapPost("/responses", async (
            [FromBody] CreateResponseRequest request,
            ResponseHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            // Foundry'nin X-Session-Id ve X-Agent-Name header'larını oku
            var context = new ResponseContext
            {
                SessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault()
                            ?? request.ConversationId,
                AgentName = httpContext.Request.Headers["X-Agent-Name"].FirstOrDefault()
                            ?? Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME"),
                Headers = httpContext.Request.Headers
                    .Where(h => h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString())
            };

            var result = await handler.CreateAsync(request, context, cancellationToken);

            // Foundry Responses protokolü yanıt formatı
            return Results.Ok(new
            {
                id = result.SessionId,
                session_id = result.SessionId,   // bir sonraki istekte conversationId olarak gönder
                output = result.Output,
                status = "completed"
            });
        })
        .WithName("Responses");

        return app;
    }
}
