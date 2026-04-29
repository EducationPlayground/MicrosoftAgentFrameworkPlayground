using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

// Result of the SigNoz log lookup for a backend ticket.
public class BackendDiagnostics
{
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public TriageResult? Triage { get; set; }
    public string SigNozRawJson { get; set; } = string.Empty;
}
