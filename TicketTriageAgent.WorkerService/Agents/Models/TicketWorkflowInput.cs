using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

// Initial message that enters the workflow: the raw ticket plus a pre-built LLM prompt.
public class TicketWorkflowInput
{
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public string Prompt { get; set; } = string.Empty;
}
