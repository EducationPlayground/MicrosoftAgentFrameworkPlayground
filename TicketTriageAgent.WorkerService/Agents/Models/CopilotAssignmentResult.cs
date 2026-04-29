using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

public class CopilotAssignmentResult
{
    public string IssueUrl { get; set; } = string.Empty;
    public int IssueNumber { get; set; }
    public bool CopilotAssigned { get; set; }
    public string AssignmentMessage { get; set; } = string.Empty;
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public TriageResult? Triage { get; set; }
}
