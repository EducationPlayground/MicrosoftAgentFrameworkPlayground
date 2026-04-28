using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace WorkflowConsole;

internal sealed class TriageExecutor : Executor
{
    private readonly AIAgent _triageAgent;
    private readonly ILogger _logger;

    public TriageExecutor(AIAgent triageAgent, ILogger logger) : base("TriageAgent")
    {
        _triageAgent = triageAgent;
        _logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .SendsMessage<TriageResultWithTicket>()
            .ConfigureRoutes(routes => routes
                .AddHandler<TicketWorkflowInput, TriageResultWithTicket>(HandleAsync));

    private async ValueTask<TriageResultWithTicket> HandleAsync(
        TicketWorkflowInput input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[TriageExecutor] Calling AIAgent.RunAsync...");
        var response = await _triageAgent.RunAsync(input.Prompt);
        _logger.LogInformation("[TriageExecutor] RunAsync returned. Response text: {Text}", response.Text);

        var triageResult = JsonSerializer.Deserialize<TriageResult>(response.Text, JsonSerializerOptions.Web);
        _logger.LogInformation("[TriageExecutor] Deserialized — SuggestedTeam={Team}", triageResult?.SuggestedTeam);

        return new TriageResultWithTicket
        {
            Triage = triageResult,
            Ticket = input.Ticket
        };
    }
}
