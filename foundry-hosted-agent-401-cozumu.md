# Foundry Hosted Agent — `HTTP 401 (PermissionDenied)` Çözümü

Azure AI Foundry'ye **Hosted Agent** olarak deploy edilen bir Microsoft Agent Framework (MAF) agent'ı, Foundry Playground'dan test edilirken `401 PermissionDenied` hatası veriyordu. Bu doküman, sorunun kök nedenini ve uygulanan çözümü kayıt altına alır.

---

## Belirti

Foundry UI'da agent'a mesaj atıldığında dönen hata:

```
Error HTTP 401 (: PermissionDenied)
The principal `07d8964c-d8c5-4c88-bad6-6527af2f365b` lacks the required data action
`Microsoft.CognitiveServices/accounts/OpenAI/responses/write`
to perform `POST /openai/v1/responses` operation.
```

Aynı hatanın generic varyantı da görülebilir:

```
Error HTTP 401 (: PermissionDenied) Principal does not have access to API/Operation.
```

Container loglarında hata, OpenAI çağrısı sırasında patlıyordu:

```
fail: Microsoft.Agents.AI.Foundry.Hosting.AgentFrameworkResponseHandler[0]
System.ClientModel.ClientResultException: HTTP 401 (: PermissionDenied)
   at OpenAI.Responses.ResponsesClient.CreateResponseAsync(...)
```

---

## Kök Neden

Hosted agent, model çağrısını yaparken **system-assigned managed identity** ile kimlik doğrular. Loglardaki `07d8964c-d8c5-4c88-bad6-6527af2f365b` principal'ı bu kimliğe ait:

```
education-test-resource-education-simpleAgent1-AgentIdentity   (Service principal)
```

Bu kimlik, **proje** scope'unda zaten `Azure AI Developer` rolüne sahipti. Ancak:

- Agent, inference çağrısını **account-level** OpenAI endpoint'ine yapıyordu: `POST /openai/v1/responses`
- Bu endpoint'in RBAC kontrolü **hesap (account)** scope'unda değerlendirilir, proje scope'unda değil
- `Azure AI Developer` rolü, gereken `Microsoft.CognitiveServices/accounts/OpenAI/responses/write` data action'ını **içermiyor**

> Azure RBAC izinleri yukarıdan aşağı miras alınır (account → project). Proje scope'una verilen rol, account-level bir çağrıya uygulanmaz.

---

## Çözüm

Managed identity'ye, **hesap (account)** kaynağı üzerinde, `responses/write` data action'ını içeren bir rol atandı.

### Seçilen rol: `Cognitive Services OpenAI Contributor`

Bu rolün dataActions değeri `Microsoft.CognitiveServices/accounts/OpenAI/*` (wildcard) olduğu için `responses/write`'ı kesin olarak kapsar.

> Alternatif olarak `Azure AI User` rolü de kullanılabilir; fakat wildcard içermesi nedeniyle `Cognitive Services OpenAI Contributor` en garantili seçenektir.

### Azure Portal üzerinden adımlar

1. **Doğru kaynağa git — kritik nokta.**
   Rol, **proje** (`education-test-resource/education`) değil, parent **hesap** (`education-test-resource`) üzerine verilmeli.
   - `lessons` resource group → içindeki kaynaklarda **Type = Azure AI Services / Foundry** olan `education-test-resource` satırını aç.
   - Doğru kaynaktaysan üst başlıkta **"Foundry project" yazmaz**; sadece `education-test-resource` ve altında "Foundry" görünür.

2. **Access control (IAM)** → **+ Add** → **Add role assignment**

3. **Role** sekmesi → `Cognitive Services OpenAI Contributor` → **Next**

4. **Members** sekmesi:
   - **Assign access to:** `User, group, or service principal`
   - **+ Select members** → agent kimliğinin object ID'si ile ara:
     ```
     07d8964c-d8c5-4c88-bad6-6527af2f365b
     ```
   - `...simpleAgent1-AgentIdentity` kimliğini seç

5. **Review + assign**

### Azure CLI alternatifi

```bash
az role assignment create \
  --assignee-object-id 07d8964c-d8c5-4c88-bad6-6527af2f365b \
  --assignee-principal-type ServicePrincipal \
  --role "Cognitive Services OpenAI Contributor" \
  --scope "/subscriptions/<SUB_ID>/resourceGroups/lessons/providers/Microsoft.CognitiveServices/accounts/education-test-resource"
```

> `--assignee-object-id` + `--assignee-principal-type ServicePrincipal` kullanmak, managed identity için Graph lookup sorunlarını atlatır.

---

## Önemli: Propagation süresi

Azure OpenAI **data-plane** RBAC atamaları, kontrol-plane'den belirgin şekilde daha yavaş yayılır. Atamadan **hemen sonra** 401 almak normaldir ve yanıltıcıdır.

- Yayılma genelde birkaç dakika, bazen **10–15 dakika** sürebilir.
- Her dakika tekrar denemek yerine, bir süre bekleyip **yeni bir konuşma** açarak test et.

---

## Doğrulama

`education-test-resource` hesabında:

- **Role assignments** sekmesinde `Cognitive Services OpenAI Contributor` altında agent identity'nin listelendiğini gör.
- **Check access** sekmesinde object ID ile arayıp rolün bu scope'ta göründüğünü teyit et.

Propagation tamamlandıktan sonra Foundry Playground'da agent normal yanıt verir.

---

## Açık Kalan / İyileştirme Notları

### 1. `/.checkpoints` yazma izni hatası (ayrı sorun)

401 çözülünce loglarda hâlâ şu görülebilir:

```
System.UnauthorizedAccessException: Access to the path '/.checkpoints' is denied.
   at Microsoft.Agents.AI.Foundry.Hosting.FileSystemAgentSessionStore.SaveSessionAsync(...)
```

Bu hata, response üretildikten **sonra**, session kaydı sırasında oluşur; görünen cevabı engellemez ama session persistence'ı bozar. Default `FileSystemAgentSessionStore`, container kökündeki `/.checkpoints` dizinine yazmaya çalışır ve non-root kullanıcı buraya yazamaz. Çözümü kod tarafında: session store'u yazılabilir bir path'e (ör. `$HOME` altına) yönlendirmek veya Foundry-managed store'a geçmek.

### 2. Endpoint formatı (önerilen yapısal düzeltme)

Mevcut kod **bare account endpoint** kullanıyor; bu yüzden inference account-level OpenAI endpoint'ine gidiyor ve account-level OpenAI rolü gerekiyor:

```csharp
// Mevcut:
new Uri("https://education-test-resource.services.ai.azure.com")

// Önerilen (MS doküman formatı):
// "https://<resource>.services.ai.azure.com/api/projects/<project>"
new Uri("https://education-test-resource.services.ai.azure.com/api/projects/education")
```

Tam **proje endpoint'i** kullanılırsa inference proje üzerinden proxy'lenir; identity'nin zaten sahip olduğu `Azure AI Developer` + `Foundry User` rolleri yeterli olur ve account-level OpenAI rolüne hiç ihtiyaç kalmaz. (Bu değişiklik redeploy gerektirir — agent version'ları immutable'dır.)

---

## Özet

| Konu | Değer |
| --- | --- |
| Hata | `HTTP 401 PermissionDenied` — `OpenAI/responses/write` |
| Principal | `07d8964c-d8c5-4c88-bad6-6527af2f365b` (agent system-assigned MI) |
| Eksik izin | `Microsoft.CognitiveServices/accounts/OpenAI/responses/write` |
| Çözüm | `Cognitive Services OpenAI Contributor` rolü |
| Scope | **Hesap** (`education-test-resource`), proje değil |
| Dikkat | Data-plane RBAC propagation 10–15 dk sürebilir |
