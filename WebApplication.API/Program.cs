using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;
using WebApplication.API.Data;
using WebApplication.API.Endpoints;
using WebApplication.API.Services;

var builder = global::Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddChatClient(openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());
builder.Services.AddEmbeddingGenerator(openAiClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator());

// Services
builder.Services.AddSingleton<PdfProcessingService>();
builder.Services.AddScoped<LlmChunkingService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<VectorSearchService>();
builder.Services.AddScoped<RagService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
app.MapDocumentEndpoints();
app.MapChatEndpoints();

app.Run();
