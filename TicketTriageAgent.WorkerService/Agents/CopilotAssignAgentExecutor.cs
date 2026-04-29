using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Workflows;
using ModelContextProtocol.Client;

namespace TicketTriageAgent.WorkerService.Agents;

internal sealed partial class CopilotAssignAgentExecutor : Executor
{
    private readonly McpClient _mcpClient;
    private readonly ILogger _logger;
    private readonly string _owner;
    private readonly string _repo;

    private static readonly Regex s_issueNumberRegex = new(
        @"/issues/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CopilotAssignAgentExecutor(McpClient mcpClient, ILogger logger, string owner, string repo)
        : base("CopilotAssignAgent")
    {
        _mcpClient = mcpClient;
        _logger = logger;
        _owner = owner;
        _repo = repo;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .SendsMessage<CopilotAssignmentResult>()
            .ConfigureRoutes(routes => routes
                .AddHandler<GitHubIssueResult, CopilotAssignmentResult>(HandleAsync));

    [MessageHandler]
    private async ValueTask<CopilotAssignmentResult> HandleAsync(
        GitHubIssueResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var issueNumber = ExtractIssueNumber(input.IssueUrl);

        if (issueNumber is null)
        {
            _logger.LogWarning(
                "[CopilotAssignAgentExecutor] Could not parse issue number from URL — IssueUrl={Url}. Skipping Copilot assignment.",
                input.IssueUrl);

            return new CopilotAssignmentResult
            {
                IssueUrl = input.IssueUrl,
                IssueNumber = 0,
                CopilotAssigned = false,
                AssignmentMessage = "Issue number could not be parsed from issue URL.",
                Ticket = input.Ticket,
                Triage = input.Triage
            };
        }

        _logger.LogInformation(
            "[CopilotAssignAgentExecutor] Assigning Copilot to issue #{IssueNumber} in {Owner}/{Repo}",
            issueNumber, _owner, _repo);

        var arguments = new Dictionary<string, object?>
        {
            ["owner"]         = _owner,
            ["repo"]          = _repo,
            ["issue_number"]  = issueNumber.Value
        };

        var result = await _mcpClient.CallToolAsync("assign_copilot_to_issue", arguments, cancellationToken: cancellationToken);

        var isError = result.IsError == true;

        var contentText = string.Join(" | ", result.Content.Select(c => c.ToString()));
        _logger.LogInformation(
            "[CopilotAssignAgentExecutor] Copilot assignment completed — IssueNumber={IssueNumber}, IsError={IsError}, Content={Content}",
            issueNumber, isError, contentText);

        return new CopilotAssignmentResult
        {
            IssueUrl = input.IssueUrl,
            IssueNumber = issueNumber.Value,
            CopilotAssigned = !isError,
            AssignmentMessage = isError
                ? $"Failed to assign Copilot to issue #{issueNumber}."
                : $"Copilot assigned to issue #{issueNumber}.",
            Ticket = input.Ticket,
            Triage = input.Triage
        };
    }

    private static int? ExtractIssueNumber(string issueUrl)
    {
        if (string.IsNullOrWhiteSpace(issueUrl))
        {
            return null;
        }

        var match = s_issueNumberRegex.Match(issueUrl);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : null;
    }
}
