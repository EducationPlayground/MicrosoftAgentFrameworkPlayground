using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

// BACKEND BRANCH — STEP 2: asks an LLM (with GitHub MCP tools) to open a structured GitHub issue.
internal sealed partial class GitHubIssueAgentExecutor : Executor
{
    private readonly AIAgent _agent;
    private readonly string _owner;
    private readonly string _repo;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false
    };

    public GitHubIssueAgentExecutor(AIAgent agent, string owner, string repo)
        : base("GitHubIssueAgent")
    {
        _agent = agent;
        _owner = owner;
        _repo = repo;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .SendsMessage<GitHubIssueResult>()
            .ConfigureRoutes(routes => routes
                .AddHandler<BackendDiagnostics, GitHubIssueResult>(HandleAsync));

    [MessageHandler]
    private async ValueTask<GitHubIssueResult> HandleAsync(
        BackendDiagnostics input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var ticketJson = JsonSerializer.Serialize(input.Ticket, s_jsonOptions);
        var triageJson = JsonSerializer.Serialize(input.Triage, s_jsonOptions);

        var prompt = $$"""
            A backend ticket has been triaged and recent error logs were retrieved from SigNoz.
            Create a GitHub issue in repository '{{_owner}}/{{_repo}}' using the GitHub MCP
            'create_issue' tool. Follow your system instructions exactly for title, body, and
            label format. Do not paraphrase the template headings.

            === TICKET (JSON) ===
            {{ticketJson}}

            === TRIAGE (JSON) ===
            {{triageJson}}

            === SIGNOZ LOGS (raw JSON; rows[].data.attributes_string contains TraceId, UserId, exception.*) ===
            {{input.SigNozRawJson}}
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);

        Console.WriteLine($"[Step 3] GitHub    → Issue created: {response.Text.Trim()}");

        return new GitHubIssueResult
        {
            IssueUrl = response.Text,
            Ticket = input.Ticket,
            Triage = input.Triage
        };
    }
}
