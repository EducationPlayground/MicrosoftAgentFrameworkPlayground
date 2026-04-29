using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace TicketTriageAgent.WorkerService.Agents;

internal sealed partial class SlackNotificationExecutor : Executor
{
    private readonly AIAgent _agent;
    private readonly ILogger _logger;

    public SlackNotificationExecutor(AIAgent agent, ILogger logger)
        : base("SlackNotification")
    {
        _agent = agent;
        _logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .YieldsOutput<string>()
            .ConfigureRoutes(routes => routes
                .AddHandler<CopilotAssignmentResult, string>(HandleAsync));

    [MessageHandler]
    private async ValueTask<string> HandleAsync(
        CopilotAssignmentResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[SlackNotificationExecutor] Sending Slack notification — TicketId={Id}, IssueUrl={Url}, CopilotAssigned={Assigned}",
            input.Ticket.Id, input.IssueUrl, input.CopilotAssigned);

        var copilotLine = input.CopilotAssigned
            ? $"Copilot assigned to issue #{input.IssueNumber} — a draft fix PR is on its way."
            : $"Copilot could NOT be assigned automatically ({input.AssignmentMessage}). Manual triage required.";

        var prompt = $"""
            A new GitHub issue was created for a triaged backend ticket and the GitHub Copilot
            coding agent has been delegated to propose a fix.

            Ticket Title  : {input.Ticket.Title}
            Severity      : {input.Triage?.Severity}
            Suggested Team: {input.Triage?.SuggestedTeam}
            GitHub Issue  : {input.IssueUrl}
            Copilot Status: {copilotLine}

            Call SendSlackNotification once to post a concise alert to the channel
            '#{input.Triage?.SuggestedTeam?.ToLowerInvariant()}-alerts'.
            The message must include the ticket title, severity level, the GitHub issue URL,
            and a one-line note about the Copilot assignment status above.
            After the tool call, confirm that the notification was sent.
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[SlackNotificationExecutor] Notification dispatched — TicketId={Id}, Result={Result}",
            input.Ticket.Id, response.Text);

        return response.Text;
    }
}
