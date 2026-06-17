using Azure.AI.AgentServer.Responses;
using Azure.AI.Projects;
using Azure.Identity;
using DeployFounderyCustomerAgentViaEmptyProject;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

var projectEndpoint = new Uri("https://education-test-resource.services.ai.azure.com");
var deployment = "gpt-5-mini";

AIAgent agent = new AIProjectClient(projectEndpoint, new DefaultAzureCredential())
    .AsAIAgent(
        model: deployment,
        instructions: "You are a helpful AI assistant.",
        name: "my-agent");

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();


app.Run();