using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

// Triage classification together with the original ticket — passed to the next stage.
public class TriageResultWithTicket
{
    public TriageResult? Triage { get; set; }
    public TicketCreatedEvent? Ticket { get; set; }
}
