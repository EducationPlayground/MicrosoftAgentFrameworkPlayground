using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkPlayground.ServiceDefaults;
using OpenAI;
using Scalar.AspNetCore;


var builder = global::Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI
builder.Services.AddOpenApi();

// Database


// OpenAI
var openAiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
    ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

var openAiClient = new OpenAIClient(openAiKey);

builder.Services.AddChatClient(openAiClient.GetChatClient("gpt-4o-mini").AsIChatClient());


var app = builder.Build();

app.MapDefaultEndpoints();

app.MapScalarApiReference();
// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints


app.Run();
