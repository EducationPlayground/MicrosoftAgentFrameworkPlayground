using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

// STAGE 1 — Classifies the incoming ticket (Category / Severity / SuggestedTeam) using an LLM.
internal sealed partial class TriageExecutor : Executor
{
    private readonly AIAgent _triageAgent;

    public TriageExecutor(AIAgent triageAgent) : base("TriageAgent")
    {
        _triageAgent = triageAgent;
    }


    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .SendsMessage<TriageResultWithTicket>()
            .ConfigureRoutes(routes => routes
                .AddHandler<TicketWorkflowInput, TriageResultWithTicket>(HandleAsync));

    [MessageHandler]
    private async ValueTask<TriageResultWithTicket> HandleAsync(
        TicketWorkflowInput input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var response = await _triageAgent.RunAsync(input.Prompt);
        var triageResult = JsonSerializer.Deserialize<TriageResult>(response.Text, JsonSerializerOptions.Web);

        Console.WriteLine($"[Step 1] Triage    → {triageResult?.Category} | {triageResult?.Severity} | Team: {triageResult?.SuggestedTeam}");

        return new TriageResultWithTicket
        {
            Triage = triageResult,
            Ticket = input.Ticket
        };
    }
}
