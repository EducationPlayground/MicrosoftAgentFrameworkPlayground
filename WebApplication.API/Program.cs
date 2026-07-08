using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

// Embedding generator used for sentiment classification (comment vs. category anchors)
builder.Services.AddEmbeddingGenerator(openAiClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator());

builder.Services.AddSingleton<WebApplication.API.Services.SentimentAnalyzer>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
WebApplication.API.Endpoints.SentimentEndpoints.MapSentimentEndpoints(app);

app.Run();
