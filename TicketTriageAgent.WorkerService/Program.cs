using TicketTriageAgent.WorkerService.Agents;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHttpClient();
builder.Services.AddHostedService<TriageAgentOrchestrator>();

var host = builder.Build();
host.Run();
