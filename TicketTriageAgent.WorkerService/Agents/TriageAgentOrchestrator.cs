using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

public class TriageResult
{
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? SuggestedTeam { get; set; }
}

public class TicketWorkflowInput
{
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public string Prompt { get; set; } = string.Empty;
}

public class TriageResultWithTicket
{
    public TriageResult? Triage { get; set; }
    public TicketCreatedEvent? Ticket { get; set; }
}

internal class TriageAgentOrchestrator(
    ILogger<TriageAgentOrchestrator> logger,
    IConnection connection,
    IHttpClientFactory httpClientFactory)
    : BackgroundService
{
    private const string ExchangeName = "ticket.created";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workflow = BuildWorkflow();

        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await SetupQueueAsync(channel, stoppingToken);

        var queueName = await BindQueueAsync(channel, stoppingToken);
        await StartConsumingAsync(channel, queueName, workflow, stoppingToken);

        logger.LogInformation(
            "TriageAgentOrchestrator started, listening on fanout exchange '{Exchange}'", ExchangeName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TriageAgentOrchestrator stopping.");
        }
    }

    private Workflow BuildWorkflow()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY")
            ?? throw new InvalidOperationException("OPEN_AI_KEY environment variable is not set.");

        var chatClient = new OpenAIClient(apiKey).GetChatClient("gpt-4o-mini");

        AIAgent triageAgent = chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                Name = "TriageAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                        You are an expert support ticket triage specialist responsible for classifying
                        incoming tickets accurately and consistently.

                        Analyze the provided ticket and determine the following:

                        1. **Category** — The nature of the issue. Choose the single best fit from:
                           - Bug: Unexpected behavior or software defect
                           - Feature Request: New functionality requested by a user
                           - Performance: Slowness, timeouts, or resource usage issues
                           - Security: Potential vulnerabilities, unauthorized access, or data exposure
                           - Question: General inquiry or clarification needed

                        2. **Severity** — The business impact of the issue. Use these definitions:
                           - Critical: System is down or data loss is occurring; immediate action required
                           - High: Major functionality is broken; significant user impact
                           - Medium: Non-critical functionality affected; workaround exists
                           - Low: Minor issue or cosmetic problem; low user impact

                        3. **SuggestedTeam** — The team best suited to handle this ticket:
                           - Backend: Server-side logic, APIs, databases
                           - Frontend: UI, client-side rendering, browser compatibility
                           - DevOps: Infrastructure, deployments, CI/CD pipelines
                           - QA: Test coverage, regression, quality assurance
                           - Product: Requirements, roadmap, prioritization decisions
                           - Security: Security incidents, penetration testing, compliance

                        Guidelines:
                        - Base your classification strictly on the ticket content provided.
                        - If the ticket is ambiguous, lean toward the higher severity.
                        - Do not infer information that is not present in the ticket.

                        Respond ONLY with a valid JSON object that strictly matches the required schema.
                        Do not include any explanation, markdown, or additional text outside the JSON.
                        """,
                    ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<TriageResult>()
                }
            });

        var triageExecutor = new TriageExecutor(triageAgent);
        var backendExecutor = new BackendSigNozExecutor(httpClientFactory, logger);
        var otherTeamExecutor = new OtherTeamExecutor(logger);

        return new WorkflowBuilder(triageExecutor)
            .AddEdge<TriageResultWithTicket>(triageExecutor, backendExecutor,
                condition: r => string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase))
            .AddEdge<TriageResultWithTicket>(triageExecutor, otherTeamExecutor,
                condition: r => !string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase))
            .WithOutputFrom(backendExecutor, otherTeamExecutor)
            .Build();
    }

    private async Task SetupQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Fanout, durable: true,
            cancellationToken: cancellationToken);
    }

    private async Task<string> BindQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var queueResult = await channel.QueueDeclareAsync(exclusive: true, autoDelete: true,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(queueResult.QueueName, ExchangeName, routingKey: string.Empty,
            cancellationToken: cancellationToken);

        return queueResult.QueueName;
    }

    private async Task StartConsumingAsync(IChannel channel, string queueName, Workflow workflow,
        CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var ticketEvent = JsonSerializer.Deserialize<TicketCreatedEvent>(json);

            if (ticketEvent is not null)
            {
                await RunTriageWorkflowAsync(workflow, ticketEvent);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer,
            cancellationToken: cancellationToken);
    }

    private async Task RunTriageWorkflowAsync(Workflow workflow, TicketCreatedEvent ticketEvent)
    {
        logger.LogInformation(
            "TriageOrchestrator received ticket — Id={Id}, Title={Title}, Priority={Priority}",
            ticketEvent.Id, ticketEvent.Title, ticketEvent.Priority);

        var prompt = $"""
            Ticket ID: {ticketEvent.Id}
            Title: {ticketEvent.Title}
            Description: {ticketEvent.Description}
            Reported Priority: {ticketEvent.Priority}
            Status: {ticketEvent.Status}
            User ID: {ticketEvent.UserId}
            Created At: {ticketEvent.CreatedAt:O}
            """;

        var input = new TicketWorkflowInput { Ticket = ticketEvent, Prompt = prompt };

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                logger.LogInformation("Workflow completed for ticket Id={Id}. Final output: {Output}",
                    ticketEvent.Id, output.Data);
            }
        }
    }
}
