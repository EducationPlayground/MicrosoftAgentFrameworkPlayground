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
                .AddHandler<GitHubIssueResult, string>(HandleAsync));

    [MessageHandler]
    private async ValueTask<string> HandleAsync(
        GitHubIssueResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[SlackNotificationExecutor] Sending Slack notification — TicketId={Id}, IssueUrl={Url}",
            input.Ticket.Id, input.IssueUrl);

        var prompt = $"""
            A new GitHub issue was created for a triaged backend ticket.

            Ticket Title : {input.Ticket.Title}
            Severity     : {input.Triage?.Severity}
            Suggested Team: {input.Triage?.SuggestedTeam}
            GitHub Issue  : {input.IssueUrl}

            Call SendSlackNotification once to post a concise alert to the channel
            '#{input.Triage?.SuggestedTeam?.ToLowerInvariant()}-alerts'.
            The message must include the ticket title, severity level, and the GitHub issue URL.
            After the tool call, confirm that the notification was sent.
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[SlackNotificationExecutor] Notification dispatched — TicketId={Id}, Result={Result}",
            input.Ticket.Id, response.Text);

        return response.Text;
    }
}
