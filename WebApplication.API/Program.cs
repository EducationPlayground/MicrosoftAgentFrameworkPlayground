using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Scalar.AspNetCore;
using WebApplication.API.Data;
using WebApplication.API.Endpoints;

var builder = global::Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

// Embedding generator (text-embedding-ada-002 → 1536 boyut)
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    openAiClient.GetEmbeddingClient("text-embedding-ada-002").AsIEmbeddingGenerator());

var app = builder.Build();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapProductEndpoints();

app.Run();
