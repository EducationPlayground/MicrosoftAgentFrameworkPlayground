using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;

namespace DeployFounderyCustomerAgentViaEmptyProject
{
    public class EchoHandler : ResponseHandler
    {
        public override IAsyncEnumerable<ResponseStreamEvent> CreateAsync(
            CreateResponse request,
            ResponseContext context,
            CancellationToken cancellationToken)
        {
            return new TextResponse(context, request,
                createText: async ct =>
                {
                    var input = await context.GetInputTextAsync(cancellationToken: ct);
                    return $"Echo: {input}";
                });
        }
    }
}
