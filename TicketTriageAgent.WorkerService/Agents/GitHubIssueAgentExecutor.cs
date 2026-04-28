using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

internal sealed partial class GitHubIssueAgentExecutor : Executor
{
    private readonly AIAgent _agent;
    private readonly ILogger _logger;
    private readonly string _owner;
    private readonly string _repo;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false
    };

    public GitHubIssueAgentExecutor(AIAgent agent, ILogger logger, string owner, string repo)
        : base("GitHubIssueAgent")
    {
        _agent = agent;
        _logger = logger;
        _owner = owner;
        _repo = repo;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .YieldsOutput<string>()
            .ConfigureRoutes(routes => routes
                .AddHandler<BackendDiagnostics, string>(HandleAsync));

    [MessageHandler]
    private async ValueTask<string> HandleAsync(
        BackendDiagnostics input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[GitHubIssueAgentExecutor] Building GitHub issue prompt — TicketId={Id}, Repo={Owner}/{Repo}",
            input.Ticket.Id, _owner, _repo);

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

        _logger.LogInformation(
            "[GitHubIssueAgentExecutor] Issue creation completed — TicketId={Id}, AgentResponse={Response}",
            input.Ticket.Id, response.Text);

        return response.Text;
    }
}
