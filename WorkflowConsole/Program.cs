using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using Shared.MessageBus;
using WorkflowConsole;

// ── Logger ────────────────────────────────────────────────────────────────────
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = loggerFactory.CreateLogger("WorkflowConsole");

// ── Simulate a TicketCreatedEvent coming off the RabbitMQ queue ───────────────
var fakeTicket = new TicketCreatedEvent
{
    Id = 42,
    Title = "API returns 500 on /orders endpoint",
    Description = "After deploying v2.3.1, the /orders endpoint started returning HTTP 500. " +
                  "Error logs show NullReferenceException in OrderService.GetByUserId().",
    Priority = "High",
    Status = "Open",
    UserId = "user-987",
    CreatedAt = DateTime.UtcNow
};

logger.LogInformation(
    "Simulating ticket from queue — Id={Id}, Title={Title}, Priority={Priority}",
    fakeTicket.Id, fakeTicket.Title, fakeTicket.Priority);

// ── Build Workflow (mirrors TriageAgentOrchestrator.BuildWorkflow) ─────────────
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

var executorLogger = loggerFactory.CreateLogger("Executor");
var triageExecutor = new TriageExecutor(triageAgent, executorLogger);
var backendExecutor = new BackendSigNozExecutor(new SingletonHttpClientFactory(), executorLogger);
var otherTeamExecutor = new OtherTeamExecutor(executorLogger);

var workflow = new WorkflowBuilder(triageExecutor)
    .AddEdge<TriageResultWithTicket>(triageExecutor, backendExecutor,
        condition: r => string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase))
    .AddEdge<TriageResultWithTicket>(triageExecutor, otherTeamExecutor,
        condition: r => !string.Equals(r?.Triage?.SuggestedTeam, "Backend", StringComparison.OrdinalIgnoreCase))
    .WithOutputFrom(backendExecutor, otherTeamExecutor)
    .Build();

// ── Run triage workflow (mirrors TriageAgentOrchestrator.RunTriageWorkflowAsync) ──
await RunTriageWorkflowAsync(workflow, fakeTicket, logger);
Console.ReadLine();

static async Task RunTriageWorkflowAsync(Workflow workflow, TicketCreatedEvent ticketEvent, ILogger logger)
{
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

    logger.LogInformation("[Step 1] Starting workflow via RunStreamingAsync...");
    StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);

    logger.LogInformation("[Step 2] Consuming events...");
    await foreach (WorkflowEvent evt in run.WatchStreamAsync())
    {
        logger.LogInformation("[ConsumeEvents] Event received: {Type}", evt.GetType().Name);

        if (evt is WorkflowOutputEvent output)
        {
            logger.LogInformation(
                "Workflow completed for ticket Id={Id}. Final output: {Output}",
                ticketEvent.Id, output.Data);
            break;
        }
    }
    logger.LogInformation("[Step 3] Workflow fully done.");
}

// ── Minimal IHttpClientFactory for the console context ───────────────────────
internal sealed class SingletonHttpClientFactory : IHttpClientFactory
{
    private static readonly HttpClient _client = new();
    public HttpClient CreateClient(string name) => _client;
}


