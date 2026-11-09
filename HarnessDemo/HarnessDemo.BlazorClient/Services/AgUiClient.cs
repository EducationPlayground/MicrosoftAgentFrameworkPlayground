using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Formatting;

namespace HarnessDemo.BlazorClient.Services;

/// <summary>
/// Saf AG-UI istemcisi: <see cref="RunAgentInput"/> gönderir, SSE akışını AG-UI olaylarına çözer.
/// Agent Framework'e bağımlılığı yoktur; sunucu AG-UI konuştuğu sürece hangi framework olduğu önemsizdir.
/// </summary>
public sealed class AgUiClient(HttpClient httpClient)
{
    private static readonly SseEventStreamFormatter Formatter = new();

    public async IAsyncEnumerable<BaseEvent> RunAsync(
        string endpoint,
        RunAgentInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(input, AGUIJsonSerializerContext.Default.RunAgentInput);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var evt in Formatter.ReadAsync(stream, cancellationToken))
        {
            yield return evt;
        }
    }
}
