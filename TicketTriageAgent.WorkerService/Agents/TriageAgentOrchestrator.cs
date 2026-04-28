using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI;
using OpenAI.Chat;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

internal class TriageAgentOrchestrator(ILogger<TriageAgentOrchestrator> logger, IConnection connection)
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
            """
            You are a ticket triage specialist. Analyze the support ticket and determine:
            1. Category (e.g., Bug, Feature Request, Performance, Security, Question)
            2. Severity (Critical, High, Medium, Low)
            3. Suggested team (e.g., Backend, Frontend, DevOps, QA, Product)
            Be concise and structured in your response.
            """,
            "TriageAgent");

        AIAgent responseAgent = chatClient.AsAIAgent(
            """
            You are a support team lead. Based on the triage analysis provided, create a brief action plan:
            1. Immediate next steps (who should act and what they should do)
            2. Estimated response time
            3. Any escalation needed
            Keep the plan short and actionable.
            """,
            "ResponseAgent");

        return AgentWorkflowBuilder.BuildSequential([triageAgent, responseAgent]);
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

        await using StreamingRun run =
            await InProcessExecution.RunStreamingAsync(workflow, new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.User, prompt));

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is AgentResponseUpdateEvent update)
            {
                logger.LogInformation("[{Agent}]: {Text}", update.ExecutorId, update.Data);
            }
            else if (evt is WorkflowOutputEvent output)
            {
                logger.LogInformation("Workflow completed for ticket Id={Id}. Final output: {Output}",
                    ticketEvent.Id, output.Data);
            }
        }
    }
}
