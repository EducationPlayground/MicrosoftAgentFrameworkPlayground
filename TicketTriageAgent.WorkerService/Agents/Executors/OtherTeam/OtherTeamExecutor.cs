using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

// NON-BACKEND BRANCH — terminal step: logs the routing decision for non-Backend/non-Frontend teams.
internal sealed partial class OtherTeamExecutor : Executor
{
    public OtherTeamExecutor() : base("OtherTeamExecutor")
    {
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .YieldsOutput<string>()
            .ConfigureRoutes(routes => routes
                .AddHandler<TriageResultWithTicket, string>(HandleAsync));


    [MessageHandler]
    private ValueTask<string> HandleAsync(
        TriageResultWithTicket input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Step 2] OtherTeam → Ticket #{input.Ticket?.Id} routed to {input.Triage?.SuggestedTeam} team (Category: {input.Triage?.Category}, Severity: {input.Triage?.Severity})");

        return ValueTask.FromResult($"Ticket #{input.Ticket?.Id} routed to {input.Triage?.SuggestedTeam} team.");
    }
}
