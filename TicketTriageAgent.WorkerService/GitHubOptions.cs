namespace TicketTriageAgent.WorkerService;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Owner { get; init; } = string.Empty;
    public string Repo { get; init; } = string.Empty;
    public string PersonalAccessToken { get; init; } = string.Empty;
}
