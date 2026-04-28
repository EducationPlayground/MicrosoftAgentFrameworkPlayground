var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.WebApplication_API>("webapplication-api");

builder.AddProject<Projects.WebApplication_Web>("webapplication-web")
    .WithReference(api);

builder.AddProject<Projects.TicketTriageAgent_WorkerService>("tickettriageagent-workerservice");

builder.Build().Run();
