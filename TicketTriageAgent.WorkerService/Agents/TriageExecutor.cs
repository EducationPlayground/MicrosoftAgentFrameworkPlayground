using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

internal sealed partial class TriageExecutor : Executor
{
    private readonly AIAgent _triageAgent;

    public TriageExecutor(AIAgent triageAgent) : base("TriageAgent")
    {
        _triageAgent = triageAgent;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder;

    [MessageHandler]
    private async ValueTask<TriageResultWithTicket> HandleAsync(
        TicketWorkflowInput input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var response = await _triageAgent.RunAsync(input.Prompt);
        var triageResult = JsonSerializer.Deserialize<TriageResult>(response.Text, JsonSerializerOptions.Web);

        return new TriageResultWithTicket
        {
            Triage = triageResult,
            Ticket = input.Ticket
        };
    }
}
