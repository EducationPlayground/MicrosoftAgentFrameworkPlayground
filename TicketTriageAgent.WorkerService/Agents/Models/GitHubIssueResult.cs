using Shared.MessageBus;

namespace TicketTriageAgent.WorkerService.Agents;

public class GitHubIssueResult
{
    public string IssueUrl { get; set; } = string.Empty;
    public TicketCreatedEvent Ticket { get; set; } = null!;
    public TriageResult? Triage { get; set; }
}
