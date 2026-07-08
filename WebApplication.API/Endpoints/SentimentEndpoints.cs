using Microsoft.AspNetCore.Http.HttpResults;
using WebApplication.API.Services;

namespace WebApplication.API.Endpoints;

public static class SentimentEndpoints
{
    public static void MapSentimentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sentiment", async Task<Results<Ok<SentimentResponse>, BadRequest<string>>>
            (SentimentRequest request, SentimentAnalyzer sentimentAnalyzer, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
            {
                return TypedResults.BadRequest("comment is required.");
            }

            var (sentiment, confidence) = await sentimentAnalyzer.ClassifyAsync(request.Comment, cancellationToken);

            return TypedResults.Ok(new SentimentResponse(request.Comment, sentiment.ToString(), confidence));
        });
    }
}

public sealed record SentimentRequest(string Comment);
public sealed record SentimentResponse(string Comment, string Sentiment, double Confidence);
