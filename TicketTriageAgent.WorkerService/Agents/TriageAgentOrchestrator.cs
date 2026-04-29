using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
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

public class BackendDiagnostics
{
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public TriageResult? Triage { get; set; }
    public string SigNozRawJson { get; set; } = string.Empty;
}

internal class TriageAgentOrchestrator(
    ILogger<TriageAgentOrchestrator> logger,
    IConnection connection,
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubOptions> gitHubOptions)
    : BackgroundService
{
    private const string ExchangeName = "ticket.created";
    private McpClient? _gitHubMcpClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workflow = await BuildWorkflowAsync(stoppingToken);

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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_gitHubMcpClient is not null)
        {
            await _gitHubMcpClient.DisposeAsync();
            _gitHubMcpClient = null;
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task<Workflow> BuildWorkflowAsync(CancellationToken cancellationToken)
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
        var gitHubIssueExecutor = await BuildGitHubIssueExecutorAsync(chatClient, cancellationToken);
        var copilotAssignExecutor = BuildCopilotAssignExecutor();
        var slackExecutor = BuildSlackNotificationExecutor(chatClient);

        return new WorkflowBuilder(triageExecutor)
            .AddEdge<TriageResultWithTicket>(triageExecutor, backendExecutor,
                condition: r => string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(r?.Triage?.SuggestedTeam, "Frontend", StringComparison.OrdinalIgnoreCase))
            .AddEdge<TriageResultWithTicket>(triageExecutor, otherTeamExecutor,
                condition: r => !string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(r?.Triage?.SuggestedTeam, "Frontend", StringComparison.OrdinalIgnoreCase))
            .AddEdge<BackendDiagnostics>(backendExecutor, gitHubIssueExecutor, condition: null)
            .AddEdge<GitHubIssueResult>(gitHubIssueExecutor, copilotAssignExecutor, condition: null)
            .AddEdge<CopilotAssignmentResult>(copilotAssignExecutor, slackExecutor, condition: null)
            .WithOutputFrom(slackExecutor, otherTeamExecutor)
            .Build();
    }

    private CopilotAssignAgentExecutor BuildCopilotAssignExecutor()
    {
        if (_gitHubMcpClient is null)
        {
            throw new InvalidOperationException(
                "GitHub MCP client must be initialized before building the Copilot assign executor.");
        }

        var gh = gitHubOptions.Value;
        var owner = gh.Owner;
        var repo = gh.Repo;

        return new CopilotAssignAgentExecutor(_gitHubMcpClient, logger, owner, repo);
    }

    private async Task<GitHubIssueAgentExecutor> BuildGitHubIssueExecutorAsync(
        ChatClient chatClient,
        CancellationToken cancellationToken)
    {
        var gh = gitHubOptions.Value;
        var owner = !string.IsNullOrWhiteSpace(gh.Owner) ? gh.Owner
            : throw new InvalidOperationException("GitHub:Owner is not configured.");
        var repo = !string.IsNullOrWhiteSpace(gh.Repo) ? gh.Repo
            : throw new InvalidOperationException("GitHub:Repo is not configured.");
        var token = !string.IsNullOrWhiteSpace(gh.PersonalAccessToken) ? gh.PersonalAccessToken
            : throw new InvalidOperationException("GitHub:PersonalAccessToken is not configured.");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "github",
            Command = "docker",
            Arguments =
            [
                "run", "-i", "--rm",
                "-e", "GITHUB_PERSONAL_ACCESS_TOKEN",
                "-e", "GITHUB_TOOLSETS",
                "ghcr.io/github/github-mcp-server"
            ],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GITHUB_PERSONAL_ACCESS_TOKEN"] = token,
                ["GITHUB_TOOLSETS"] = "default,copilot"
            }
        });

        _gitHubMcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var mcpTools = await _gitHubMcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        var toolNames = string.Join(", ", mcpTools.Select(t => t.Name).ToArray());
        logger.LogInformation(
            "[TriageAgentOrchestrator] GitHub MCP server connected — {ToolCount} tools available: {Tools}",
            mcpTools.Count, toolNames);

        var instructions = $$"""
            You are a release-engineering assistant that turns a triaged backend support ticket and
            its recent error logs into a high-quality GitHub issue.

            Always create the issue in the repository '{{owner}}/{{repo}}' using the GitHub MCP
            'create_issue' tool. Do not invent any other tool name — only call tools that were
            registered with you.

            From the SigNoz log JSON, extract for the most relevant error row:
              - attributes_string.TraceId          -> Trace Id
              - attributes_string.UserId           -> User Id
              - attributes_string.exception.type   -> Error Type
              - attributes_string.exception.message -> Error Message
              - attributes_string.exception.stacktrace -> Stack Trace
              - attributes_string.RequestPath      -> Request Path
              - resources_string."service.name"    -> Service
              - timestamp                          -> Timestamp
            If a field cannot be found, write "(unknown)".

            Issue title format: "[<Severity>] <Ticket Title> (Ticket #<Id>)"

            Issue body MUST be Markdown using exactly this template:

            ## Summary
            <one short paragraph describing the failure and likely root cause>

            ## Triage
            - Category: <category>
            - Severity: <severity>
            - Suggested Team: <team>

            ## Correlation
            - Ticket Id: <id>
            - User Id: <user id>
            - Trace Id: <trace id>
            - Service: <service>
            - Request Path: <request path>
            - Timestamp: <timestamp>

            ## Error
            **Type:** <error type>
            **Message:** <error message>

            ```
            <stack trace>
            ```

            ## Suggested Next Steps
            - <actionable step 1>
            - <actionable step 2>

            Labels: ["bug", "auto-triaged", "<suggested-team-lowercased>"]

            After the tool call succeeds, respond with the created issue URL only.
            """;

        AIAgent gitHubAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "GitHubIssueAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [.. mcpTools]
            }
        });

        return new GitHubIssueAgentExecutor(gitHubAgent, logger, owner, repo);
    }

    private SlackNotificationExecutor BuildSlackNotificationExecutor(ChatClient chatClient)
    {
        var sendSlackNotification = AIFunctionFactory.Create(
            (string channel, string message) =>
            {
                logger.LogInformation("[SLACK → #{Channel}] {Message}", channel, message);
                Console.WriteLine($"[SLACK → #{channel}] {message}");
                return "Notification delivered to Slack channel successfully.";
            },
            name: "SendSlackNotification",
            description: "Sends a notification message to the specified Slack channel.");

        AIAgent slackAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SlackNotificationAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a team notification assistant. When a GitHub issue is created for a
                    triaged backend ticket, you send a concise alert to the relevant Slack channel.
                    Always call SendSlackNotification exactly once with the channel name (without #)
                    and a clear, brief message. After the tool call, confirm the notification was sent.
                    """,
                Tools = [sendSlackNotification]
            }
        });

        return new SlackNotificationExecutor(slackAgent, logger);
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

        // WatchStreamAsync must begin iterating BEFORE TrySendMessageAsync is called,
        // otherwise events emitted synchronously during processing will be missed.
        var watchTask = ConsumeWorkflowEventsAsync(run, ticketEvent.Id);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await watchTask;
    }

    private async Task ConsumeWorkflowEventsAsync(StreamingRun run, int ticketId)
    {
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                logger.LogInformation("Workflow completed for ticket Id={Id}. Final output: {Output}",
                    ticketId, output.Data);
            }
        }
    }
}
