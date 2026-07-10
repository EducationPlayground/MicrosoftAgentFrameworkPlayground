# Hybrid Search & Reciprocal Rank Fusion (RRF)

Bu doküman `/products/hybrid-search` endpoint'indeki **RRF (Reciprocal Rank Fusion)** adımını ve neden `k = 60` kullandığımızı açıklar.

## Problem: İki farklı arama sonucunu nasıl birleştiririz?

Hybrid search iki ayrı sinyal üretir:

1. **Vector search** — anlamsal (semantic) benzerlik, cosine distance ile sıralanır.
   - Örn: "kulaklık" arayınca "Noise Cancelling Earbuds" anlamca yakındır ama kelime birebir geçmez.
2. **Keyword search** — birebir kelime eşleşmesi (`LIKE '%...%'`).
   - Örn: "USB" arayınca içinde "USB" geçen ürünler bulunur.

Bu iki listenin **skorları farklı ölçeklerdedir** (biri 0–2 arası cosine distance, diğeri sadece "eşleşti/eşleşmedi"). Bu yüzden skorları doğrudan toplayamayız. İşte burada **RRF** devreye girer.

## RRF Fikri: Skoru değil, SIRALAMAYI (rank) kullan

RRF, her sonucun **listedeki sırasını** (0. sıra, 1. sıra, ...) kullanır — ham skoru değil. Böylece farklı ölçeklerdeki iki aramayı adil şekilde birleştiririz.

Her sonuç için formül:

$$
\text{RRF}(d) = \sum_{r \in \text{listeler}} \frac{1}{k + \text{rank}_r(d)}
$$

- `rank` = o listede kaçıncı sırada (0'dan başlar).
- `k` = yumuşatma (smoothing) sabiti.
- Bir doküman birden fazla listede yer alıyorsa, katkıları **toplanır** → hem anlamca hem kelimece uyanlar üste çıkar.

### Kodda karşılığı

```csharp
scores[id] = scores.GetValueOrDefault(id) + 1.0 / (k + i + 1);
```

- `i` = döngü indeksi (0-tabanlı rank).
- `+ 1` ekliyoruz çünkü ilk sıra (i = 0) için payda `k + 0 + 1` olur; böylece **0'a bölme** riski olmadan ilk sıra en yüksek katkıyı alır.

## Neden `k = 60`?

`k` sabiti, üst sıralar ile alt sıralar arasındaki **katkı farkını** kontrol eder:

| k değeri | Davranış |
| --- | --- |
| Küçük k (örn. 1) | İlk sıra çok baskın olur, alt sıralar neredeyse yok sayılır. Tek listeye aşırı güven. |
| Büyük k (örn. 60) | Sıralar arası fark yumuşar; alt sıradaki sonuçlar da söz sahibi olur. Listeler arası **denge** iyi olur. |
| Çok büyük k | Tüm sıralar neredeyse eşitlenir, sıralamanın anlamı kaybolur. |

`k = 60` değeri, RRF'i tanıtan akademik makaleden (Cormack, Clarke & Büttcher, 2009 — *"Reciprocal Rank Fusion outperforms Condorcet..."*) gelir. Deneysel olarak birçok arama senaryosunda iyi sonuç verdiği için **fiili standart (de-facto default)** haline gelmiştir. Elasticsearch, OpenSearch gibi sistemler de varsayılan olarak 60 kullanır.

### Sayısal örnek (k = 60)

| Rank (i) | Payda (k + i + 1) | Katkı (1 / payda) |
| --- | --- | --- |
| 0 | 61 | 0.01639 |
| 1 | 62 | 0.01613 |
| 2 | 63 | 0.01587 |
| 19 | 80 | 0.01250 |

Görüldüğü gibi 1. sıra ile 20. sıra arasındaki fark küçük ama var. Bu da "üst sıra önemli ama alt sıralar da katkı sağlasın" dengesini kurar.

## Adım adım kod akışı

```csharp
// 1. Her listeyi gezip RRF katkısını topla
for (var i = 0; i < vectorResults.Count; i++)
    scores[vectorResults[i].Id] += 1.0 / (k + i + 1);

for (var i = 0; i < keywordResults.Count; i++)
    scores[keywordResults[i].Id] += 1.0 / (k + i + 1);

// 2. Id -> Name eşlemesi (skor sözlüğünde isim yok)
var nameMap = vectorResults.Concat(keywordResults)
    .GroupBy(r => r.Id)
    .ToDictionary(g => g.Key, g => g.First().Name);

// 3. Toplam RRF skoruna göre sırala, en iyi 5'i al
var results = scores
    .Select(kv => new { Id = kv.Key, Name = nameMap[kv.Key], RrfScore = kv.Value })
    .OrderByDescending(r => r.RrfScore)
    .Take(5)
    .ToList();
```

## Özet

- **Neden RRF?** Farklı ölçeklerdeki iki arama sonucunu (vektör + keyword) adil birleştirmek için.
- **Neden rank kullanır?** Ham skorlar kıyaslanamaz; sıralama kıyaslanabilir.
- **Neden `+ 1`?** 0'a bölmeyi engellemek ve ilk sıraya en yüksek katkıyı vermek için.
- **Neden `k = 60`?** Akademik makaleden gelen, üst/alt sıra dengesini iyi kuran ve endüstride standart kabul edilen değer.
