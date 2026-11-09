# HarnessDemo: Agent ile Harness Agent karşılaştırması

İki proje var:

| Proje | Görevi |
|---|---|
| `HarnessDemo.AgentServer` | Microsoft Agent Framework + AG-UI sunucusu. `POST /agent` normal agent'ı, `POST /harness` harness agent'ı sunar. |
| `HarnessDemo.BlazorClient` | Blazor Server istemcisi. Agent Framework'e bağımlı değildir; sadece AG-UI protokolünü konuşur (`RunAgentInput` gönderir, SSE olaylarını okur). |

İki agent da aynı modeli, aynı talimatı ve aynı sipariş araçlarını kullanır. Tek fark sunucuda agent'ın `AsAIAgent` mı `AsHarnessAgent` mı ile oluşturulmasıdır.

## Çalıştırma

Aspire ile (önerilen):

```bash
cd HarnessDemo.AppHost
dotnet user-secrets set "Parameters:openrouter-key" "sk-or-..."   # ya da dashboard'da girilir
aspire start
```

Dashboard'da `agentserver` için `invoke_agent` ve `chat` span'leri görünür. Sadece harness agent'ta görünür:
harness OpenTelemetry sarmalayıcısını varsayılan getirir, normal agent getirmez.

Aspire olmadan:

```bash
export OPENROUTER_API_KEY=sk-or-...   # model: appsettings.json → OpenRouter:Model (varsayılan anthropic/claude-haiku-5.5)
dotnet run --project HarnessDemo.AgentServer   # http://localhost:5275
dotnet run --project HarnessDemo.BlazorClient  # http://localhost:5133
```

Sayfalar: `/agent`, `/harness`, `/compare` (aynı mesajı iki agent'a birlikte gönderir).

## Demo akışı

1. **Tek sipariş** · ikisi de benzer davranır, kısa işte harness gerekmez.
2. **Olmayan kayıt** · ikisi de "bulunamadı" demeli.
3. **Uzun görev** · harness plan modunda `todos_add` ile iş listesi çıkarır, sağ panelde todo'lar görünür.
4. **Planı uygula** · harness `mode_set` ile execute moduna geçer, todo'ları tek tek kapatır.
5. **Onaylı e-posta** · iki agent'ta da AG-UI interrupt ile onay kartı çıkar.
6. **Hatırla** · harness `file_memory_*` araçlarıyla `HarnessDemo.AgentServer/agent-memory/` altına not yazar.

## Mimari notlar

- **Server-side session:** `WithInMemorySessionStore` ile oturum `threadId` üzerinden korunur. İstemci her turda yalnızca yeni mesajı ve `parentRunId` gönderir. İzolasyon yerel demo için kapalıdır; production'da kalıcı store ve kimliğe bağlı izolasyon gerekir.
- **Harness durumu → UI:** `HarnessStateAgent` todo listesini ve modu her ilgili araç çağrısından sonra okur, `DataContent` olarak yayınlar. `AGUIStreamOptions.MapContent` bunu `STATE_SNAPSHOT` olayına çevirir.
- **Onay resume formatı:** istemci `resume[].payload` içinde `{ approved, toolCall: { callId, name, arguments } }` gönderir.
- **Normal agent'ta `RequirePerServiceCallChatHistoryPersistence = true`:** bu olmadan AG-UI resume'u bekleyen onay isteğini bulamıyor. Harness bunu varsayılan olarak açık getirir; sunumda iyi bir "harness hazır getirir" örneği.
- **Compaction kapalı:** 1.28.0'da `MaxContextWindowTokens` verildiğinde hosted session ile sonraki turların mesajları modele ulaşmıyor (experimental özellik).
- **Model:** OpenRouter (OpenAI uyumlu endpoint `https://openrouter.ai/api/v1`), varsayılan `anthropic/claude-haiku-5.5`.
- **Web search kapalı:** hosted web search OpenAI'ın `web_search_options` alanına çevrilir; OpenRouter sağlayıcıları bunu araçlarla birlikte sunmuyor.
