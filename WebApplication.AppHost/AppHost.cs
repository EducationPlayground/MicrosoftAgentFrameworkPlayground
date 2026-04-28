var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var api = builder.AddProject<Projects.WebApplication_API>("webapplication-api")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.WebApplication_Web>("webapplication-web")
    .WithReference(api);

builder.AddProject<Projects.TicketTriageAgent_WorkerService>("tickettriageagent-workerservice")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.Build().Run();
