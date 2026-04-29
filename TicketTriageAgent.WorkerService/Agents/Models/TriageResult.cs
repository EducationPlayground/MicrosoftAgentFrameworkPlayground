namespace TicketTriageAgent.WorkerService.Agents;

// Output of the TriageAgent — the LLM classifies the incoming ticket.
public class TriageResult
{
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? SuggestedTeam { get; set; }
}
