namespace WorkflowConsole;

public class TriageResult
{
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? SuggestedTeam { get; set; }
}

public class TicketWorkflowInput
{
    public Shared.MessageBus.TicketCreatedEvent Ticket { get; set; } = null!;
    public string Prompt { get; set; } = string.Empty;
}

public class TriageResultWithTicket
{
    public TriageResult? Triage { get; set; }
    public Shared.MessageBus.TicketCreatedEvent? Ticket { get; set; }
}
