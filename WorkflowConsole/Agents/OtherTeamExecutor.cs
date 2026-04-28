using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace WorkflowConsole;

internal sealed class OtherTeamExecutor : Executor
{
    private readonly ILogger _logger;

    public OtherTeamExecutor(ILogger logger) : base("OtherTeamExecutor")
    {
        _logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .YieldsOutput<string>()
            .ConfigureRoutes(routes => routes
                .AddHandler<TriageResultWithTicket, string>(HandleAsync));

    private async ValueTask<string> HandleAsync(
        TriageResultWithTicket input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var output = $"""
            [OtherTeamExecutor] Ticket routed to non-Backend team
            Ticket Id     : {input.Ticket?.Id}
            Title         : {input.Ticket?.Title}
            Category      : {input.Triage?.Category}
            Severity      : {input.Triage?.Severity}
            Suggested Team: {input.Triage?.SuggestedTeam}
            """;

        Console.WriteLine(output);

        _logger.LogInformation(
            "[OtherTeamExecutor] TicketId={Id}, Team={Team}, Category={Category}, Severity={Severity}",
            input.Ticket?.Id, input.Triage?.SuggestedTeam, input.Triage?.Category, input.Triage?.Severity);

        await context.YieldOutputAsync(output, cancellationToken);
        return output;
    }
}
