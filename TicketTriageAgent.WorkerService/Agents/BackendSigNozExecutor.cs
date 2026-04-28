using System.Net.Http.Json;
using Microsoft.Agents.AI.Workflows;

namespace TicketTriageAgent.WorkerService.Agents;

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

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => protocolBuilder;

    [MessageHandler]
    private async ValueTask<string> HandleAsync(
        TriageResultWithTicket input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient();

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

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
