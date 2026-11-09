using System.Text;
using System.Text.Json;
using AGUI.Abstractions;

namespace HarnessDemo.BlazorClient.Services;

/// <summary>
/// Bir AG-UI thread'inin istemci tarafı durumu: mesajlar, araç çağrıları, onay istekleri ve
/// sunucunun STATE_SNAPSHOT ile gönderdiği harness durumu.
/// Server-side session kullanıldığı için her istekte yalnızca yeni mesaj gönderilir;
/// devamlılık <c>threadId</c> + <c>parentRunId</c> ile sağlanır.
/// </summary>
public sealed class AgentChat(AgUiClient client, string endpoint)
{
    private const int MaxEventLog = 300;

    private readonly Dictionary<string, ToolCallItem> _toolCalls = [];
    private readonly Dictionary<string, AssistantItem> _messages = [];
    private string? _lastRunId;

    public string Endpoint { get; } = endpoint;
    public string ThreadId { get; private set; } = NewId();
    public List<ChatItem> Items { get; } = [];
    public List<EventLogEntry> EventLog { get; } = [];
    public HarnessSnapshot? State { get; private set; }
    public bool IsRunning { get; private set; }
    public int RunCount { get; private set; }
    public int ToolCallCount => _toolCalls.Count;

    public event Action? Changed;

    public Task SendAsync(string text)
    {
        var message = new AGUIUserMessage { Id = NewId(), Content = text };
        Items.Add(new UserItem(text));

        return RunAsync(new RunAgentInput
        {
            ThreadId = ThreadId,
            RunId = NewId(),
            ParentRunId = _lastRunId,
            Messages = [message],
        });
    }

    public Task ResolveApprovalAsync(ApprovalItem approval, bool accepted)
    {
        approval.Accepted = accepted;

        // .NET AG-UI adaptörünün beklediği resume payload'ı: karar + onaylanan araç çağrısı.
        // Sunucu bu çağrıyı kendi kaydettiği istekle eşleştirir (approval response binding).
        var payload = JsonSerializer.SerializeToElement(new
        {
            approved = accepted,
            toolCall = new
            {
                callId = approval.ToolCallId,
                name = approval.ToolName,
                arguments = ParseArguments(approval.Arguments),
            },
        });

        return RunAsync(new RunAgentInput
        {
            ThreadId = ThreadId,
            RunId = NewId(),
            ParentRunId = _lastRunId,
            Messages = [],
            Resume = [new AGUIResume { InterruptId = approval.InterruptId, Status = ResumeStatus.Resolved, Payload = payload }],
        });
    }

    public void Reset()
    {
        ThreadId = NewId();
        _lastRunId = null;
        _toolCalls.Clear();
        _messages.Clear();
        Items.Clear();
        EventLog.Clear();
        State = null;
        RunCount = 0;
        Changed?.Invoke();
    }

    private async Task RunAsync(RunAgentInput input)
    {
        IsRunning = true;
        RunCount++;
        Changed?.Invoke();

        try
        {
            await foreach (var evt in client.RunAsync(Endpoint, input))
            {
                Apply(evt);
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Items.Add(new ErrorItem($"Bağlantı hatası: {ex.Message}"));
        }
        finally
        {
            IsRunning = false;
            Changed?.Invoke();
        }
    }

    private void Apply(BaseEvent evt)
    {
        switch (evt)
        {
            case RunStartedEvent e:
                _lastRunId = e.RunId;
                // Mesaj kimlikleri run kapsamında eşleştirilir; önceki run'ın balonuna yazılmasın.
                _messages.Clear();
                Log(evt, $"run={Short(e.RunId)}");
                break;

            case TextMessageStartEvent e:
                GetOrAddMessage(e.MessageId);
                Log(evt);
                break;

            case TextMessageContentEvent e:
                GetOrAddMessage(e.MessageId).Text.Append(e.Delta);
                // Metin parçaları çok sık gelir; log'u şişirmemek için kaydetmiyoruz.
                break;

            case TextMessageEndEvent:
                Log(evt);
                break;

            case ToolCallStartEvent e:
                if (_toolCalls.TryGetValue(e.ToolCallId, out var existing))
                {
                    // Onay sonrası resume, aynı çağrıyı yeniden yayınlar; mevcut kartı güncelliyoruz.
                    existing.Arguments.Clear();
                }
                else
                {
                    var call = new ToolCallItem(e.ToolCallId, e.ToolCallName);
                    _toolCalls[e.ToolCallId] = call;
                    Items.Add(call);
                }
                Log(evt, e.ToolCallName);
                break;

            case ToolCallArgsEvent e:
                if (_toolCalls.TryGetValue(e.ToolCallId, out var argsCall))
                {
                    argsCall.Arguments.Append(e.Delta);
                }
                break;

            case ToolCallEndEvent:
                Log(evt);
                break;

            case ToolCallResultEvent e:
                if (_toolCalls.TryGetValue(e.ToolCallId, out var resultCall))
                {
                    resultCall.Result = e.Content.IsText ? e.Content.Value as string : e.Content.Value?.ToString();
                    Log(evt, resultCall.Name);
                }
                else
                {
                    Log(evt);
                }
                break;

            case StateSnapshotEvent e:
                State = HarnessSnapshot.TryParse(e.Snapshot);
                Log(evt, State is null ? null : $"mode={State.Mode}, todos={State.Todos.Count}");
                break;

            case RunFinishedEvent e:
                if (e.Outcome is RunFinishedInterruptOutcome interrupt)
                {
                    foreach (var i in interrupt.Interrupts)
                    {
                        _toolCalls.TryGetValue(i.ToolCallId ?? string.Empty, out var pending);
                        pending?.MarkAwaitingApproval();
                        Items.Add(new ApprovalItem(i.Id, i.ToolCallId, pending?.Name ?? "?", pending?.Arguments.ToString(), i.Message));
                    }
                    Log(evt, $"interrupt ×{interrupt.Interrupts.Count}");
                }
                else
                {
                    Log(evt, e.Outcome?.Type ?? "success");
                }
                break;

            case RunErrorEvent e:
                Items.Add(new ErrorItem(e.Message ?? "Bilinmeyen hata"));
                Log(evt, e.Code);
                break;

            default:
                Log(evt);
                break;
        }
    }

    private AssistantItem GetOrAddMessage(string messageId)
    {
        if (!_messages.TryGetValue(messageId, out var item))
        {
            item = new AssistantItem(messageId);
            _messages[messageId] = item;
            Items.Add(item);
        }

        return item;
    }

    private void Log(BaseEvent evt, string? detail = null)
    {
        EventLog.Add(new EventLogEntry(DateTime.Now, evt.Type, detail));
        if (EventLog.Count > MaxEventLog)
        {
            EventLog.RemoveAt(0);
        }
    }

    private static JsonElement ParseArguments(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? JsonSerializer.SerializeToElement(new { }) : JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string Short(string? id) => id is { Length: > 8 } ? id[..8] : id ?? "";
}

public abstract record ChatItem;

public sealed record UserItem(string Text) : ChatItem;

public sealed record AssistantItem(string MessageId) : ChatItem
{
    public StringBuilder Text { get; } = new();
}

public sealed record ToolCallItem(string Id, string Name) : ChatItem
{
    public StringBuilder Arguments { get; } = new();
    public string? Result { get; set; }
    public bool AwaitingApproval { get; private set; }

    /// <summary>Harness'in kendi altyapı araçları (planlama, mod, bellek).</summary>
    public bool IsHarnessTool => Name.StartsWith("todos_") || Name.StartsWith("mode_") || Name.StartsWith("file_memory_");

    public void MarkAwaitingApproval() => AwaitingApproval = true;
}

public sealed record ApprovalItem(string InterruptId, string? ToolCallId, string ToolName, string? Arguments, string? Message) : ChatItem
{
    public bool? Accepted { get; set; }
}

public sealed record ErrorItem(string Message) : ChatItem;

public sealed record EventLogEntry(DateTime At, string Type, string? Detail);

public sealed record TodoView(int Id, string Title, string? Description, bool IsComplete);

public sealed record HarnessSnapshot(string? Mode, IReadOnlyList<TodoView> Todos)
{
    public static HarnessSnapshot? TryParse(JsonElement? snapshot)
    {
        if (snapshot is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        var mode = root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        List<TodoView> todos = [];
        if (root.TryGetProperty("todos", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in list.EnumerateArray())
            {
                todos.Add(new TodoView(
                    t.TryGetProperty("id", out var id) && id.TryGetInt32(out var idValue) ? idValue : 0,
                    t.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    t.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() : null,
                    t.TryGetProperty("isComplete", out var done) && done.ValueKind == JsonValueKind.True));
            }
        }

        return new HarnessSnapshot(mode, todos);
    }
}
