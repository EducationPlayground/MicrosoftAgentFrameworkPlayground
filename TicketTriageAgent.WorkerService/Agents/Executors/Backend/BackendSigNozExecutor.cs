using System.Net.Http.Json;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

// BACKEND BRANCH — STEP 1: pulls the most recent error logs for the ticket's user from SigNoz.
internal sealed partial class BackendSigNozExecutor : Executor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private const string SigNozUrl = "http://localhost:8080/api/v5/query_range";

    public BackendSigNozExecutor(IHttpClientFactory httpClientFactory, ILogger logger)
        : base("BackendSigNozExecutor")
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .SendsMessage<BackendDiagnostics>()
            .ConfigureRoutes(routes => routes
                .AddHandler<TriageResultWithTicket, BackendDiagnostics>(HandleAsync));

    [MessageHandler]
    private async ValueTask<BackendDiagnostics> HandleAsync(
        TriageResultWithTicket input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Remove("SIGNOZ-API-KEY");
        httpClient.DefaultRequestHeaders.Add("SIGNOZ-API-KEY", "Y8k8Vh9t4cDImAn/xUaV5g9ZM+XwvUCboqtopggznto=");
        var createdAt = DateTime.SpecifyKind(input.Ticket!.CreatedAt, DateTimeKind.Utc);
        var ticketOffset = new DateTimeOffset(createdAt);
        var start = ticketOffset.AddHours(-24).ToUnixTimeMilliseconds();
        var end = ticketOffset.ToUnixTimeMilliseconds();

        var requestBody = new
        {
            start,
            end,
            requestType = "raw",
            variables = new { },
            compositeQuery = new
            {
                queries = new[]
                {
                    new
                    {
                        type = "builder_query",
                        spec = new
                        {
                            name = "A",
                            signal = "logs",
                            filter = new
                            {
                                expression = $"service.name = 'webapplication-api' AND severity_text='Error' AND UserId='{input.Ticket.UserId}'"
                            },
                            order = new object[]
                            {
                                new { key = new { name = "timestamp" }, direction = "desc" },
                                new { key = new { name = "id" }, direction = "desc" }
                            },
                            offset = 0,
                            limit = 10
                        }
                    }
                }
            }
        };

        var response = await httpClient.PostAsJsonAsync(SigNozUrl, requestBody, cancellationToken);

        _logger.LogInformation(
            "[BackendSigNozExecutor] SigNoz query sent — TicketId={Id}, UserId={UserId}, StatusCode={StatusCode}",
            input.Ticket.Id, input.Ticket.UserId, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

        return new BackendDiagnostics
        {
            Ticket = input.Ticket,
            Triage = input.Triage,
            SigNozRawJson = rawJson
        };
    }
}
