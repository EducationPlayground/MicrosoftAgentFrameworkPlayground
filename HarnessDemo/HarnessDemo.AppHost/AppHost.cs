var builder = DistributedApplication.CreateBuilder(args);

// OpenRouter anahtarı: user secrets (Parameters:openrouter-key) veya dashboard'dan girilir.
var openRouterKey = builder.AddParameter("openrouter-key", secret: true);

var agentServer = builder.AddProject<Projects.HarnessDemo_AgentServer>("agentserver")
    .WithEnvironment("OPENROUTER_API_KEY", openRouterKey);

builder.AddProject<Projects.HarnessDemo_BlazorClient>("blazorclient")
    .WithReference(agentServer)
    .WaitFor(agentServer)
    // AG-UI istemcisi sunucuyu service discovery ile bulur.
    .WithEnvironment("AgentServer__BaseUrl", "https+http://agentserver/")
    .WithExternalHttpEndpoints();

builder.Build().Run();
