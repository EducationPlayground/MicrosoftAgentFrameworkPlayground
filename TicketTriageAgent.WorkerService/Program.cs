using TicketTriageAgent.WorkerService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
