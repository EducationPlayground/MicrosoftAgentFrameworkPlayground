using TicketTriageAgent.WorkerService;
using TicketTriageAgent.WorkerService.Agents;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHttpClient();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.AddHostedService<TicketCreatedConsumer>();

var host = builder.Build();
host.Run();
