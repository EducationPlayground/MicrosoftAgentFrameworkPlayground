using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Agents;

/// <summary>
/// Harness'in oturumda tuttuğu iş durumunu (todo listesi ve çalışma modu) istemciye görünür yapar.
/// Harness bu durumu sadece session içinde saklar; burada her ilgili araç çağrısından sonra ve
/// çalıştırmanın başında/sonunda <see cref="MediaType"/> türünde bir <see cref="DataContent"/> üretilir.
/// AG-UI katmanı bu içeriği <c>STATE_SNAPSHOT</c> olayına çevirir (bkz. Program.cs, MapContent).
/// Yerleşik bir içerik tipi kullanıyoruz çünkü hosting katmanı her update'i JSON'a serileştirir.
/// </summary>
public sealed class HarnessStateAgent(AIAgent innerAgent, TodoProvider todoProvider, AgentModeProvider modeProvider)
    : DelegatingAIAgent(innerAgent)
{
    public const string MediaType = "application/vnd.harness-state+json";

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (session is not null)
        {
            yield return await CreateStateUpdateAsync(session, cancellationToken);
        }

        Dictionary<string, string> callNames = [];

        await foreach (var update in InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken))
        {
            yield return update;

            if (session is null)
            {
                continue;
            }

            var stateChanged = false;
            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    callNames[call.CallId] = call.Name;
                }
                else if (content is FunctionResultContent result &&
                         callNames.TryGetValue(result.CallId, out var name) &&
                         (name.StartsWith("todos_", StringComparison.Ordinal) || name.StartsWith("mode_", StringComparison.Ordinal)))
                {
                    stateChanged = true;
                }
            }

            if (stateChanged)
            {
                yield return await CreateStateUpdateAsync(session, cancellationToken);
            }
        }

        if (session is not null)
        {
            yield return await CreateStateUpdateAsync(session, cancellationToken);
        }
    }

    private async Task<AgentResponseUpdate> CreateStateUpdateAsync(AgentSession session, CancellationToken cancellationToken)
    {
        var todos = await todoProvider.GetAllTodosAsync(session, cancellationToken);
        var mode = await modeProvider.GetModeAsync(session, cancellationToken);

        var snapshot = JsonSerializer.SerializeToUtf8Bytes(new
        {
            mode,
            todos = todos.Select(t => new { t.Id, t.Title, t.Description, t.IsComplete }),
        }, JsonSerializerOptions.Web);

        return new AgentResponseUpdate(ChatRole.Assistant, [new DataContent(snapshot, MediaType)]);
    }
}
