using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

/// <summary>
/// Sentiment categories a comment can be classified into.
/// </summary>
public enum Sentiment
{
    Olumlu,
    Olumsuz,
    Notr
}

/// <summary>
/// Classifies Turkish comments as Olumlu (positive), Olumsuz (negative) or Notr (neutral) using
/// text embeddings only (no chat/LLM call). Each category is represented by the centroid of a
/// few example sentences; a comment is classified by picking the category whose centroid has the
/// highest cosine similarity to the comment's own embedding.
/// </summary>
public class SentimentAnalyzer
{
    private static readonly Dictionary<Sentiment, string[]> CategoryExamples = new()
    {
        [Sentiment.Olumlu] =
        [
            "Bu ürünü çok beğendim, harika bir deneyimdi.",
            "Kesinlikle tavsiye ederim, mükemmel bir hizmet aldım.",
            "Çok memnun kaldım, elime çok hızlı ulaştı.",
            "Beklentimin üzerinde, gayet kaliteli ve kullanışlı.",
            "Teşekkürler, tam istediğim gibiydi, çok mutluyum."
        ],
        [Sentiment.Olumsuz] =
        [
            "Çok kötü bir deneyimdi, hiç memnun kalmadım.",
            "Kesinlikle tavsiye etmiyorum, berbat bir hizmetti.",
            "Ürün kırık geldi, iade sürecinde çok zorlandım.",
            "Paramın karşılığını alamadım, çok kötü kalitede.",
            "İlgisiz bir müşteri hizmeti, bir daha asla almam."
        ],
        [Sentiment.Notr] =
        [
            "Ürün açıklandığı gibi geldi, standart bir deneyimdi.",
            "Ne iyi ne kötü, ortalama bir üründü.",
            "Kargo zamanında geldi, ürün hakkında özel bir görüşüm yok.",
            "Fiyatı ve kalitesi beklediğim gibiydi, sıradan.",
            "Kullanımı basit, dikkat çekici bir yanı yok."
        ]
    };

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly Lazy<Task<Dictionary<Sentiment, float[]>>> _categoryCentroids;

    public SentimentAnalyzer(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
        _categoryCentroids = new Lazy<Task<Dictionary<Sentiment, float[]>>>(BuildCategoryCentroidsAsync);
    }

    /// <summary>
    /// Classifies the given comment and returns the predicted sentiment along with the cosine
    /// similarity score of the winning category (typically in the 0-1 range).
    /// </summary>
    public async Task<(Sentiment Sentiment, double Confidence)> ClassifyAsync(string comment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("Comment must not be empty.", nameof(comment));
        }

        var centroids = await _categoryCentroids.Value.WaitAsync(cancellationToken);
        var commentVector = await _embeddingGenerator.GenerateVectorAsync(comment, cancellationToken: cancellationToken);

        var bestSentiment = Sentiment.Notr;
        var bestScore = float.NegativeInfinity;

        foreach (var (sentiment, centroid) in centroids)
        {
            var score = CosineSimilarity(commentVector.Span, centroid);
            if (score > bestScore)
            {
                bestScore = score;
                bestSentiment = sentiment;
            }
        }

        return (bestSentiment, bestScore);
    }

    // Builds one centroid embedding per sentiment category by averaging the embeddings of its
    // example sentences. Computed once (lazily) and reused for every classification request.
    private async Task<Dictionary<Sentiment, float[]>> BuildCategoryCentroidsAsync()
    {
        var centroids = new Dictionary<Sentiment, float[]>();

        foreach (var (sentiment, examples) in CategoryExamples)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(examples);

            int dimensions = embeddings[0].Vector.Length;
            var centroid = new float[dimensions];

            // Sum every example embedding element-by-element.
            foreach (var embedding in embeddings)
            {
                var vector = embedding.Vector.Span;
                for (int i = 0; i < dimensions; i++)
                {
                    centroid[i] += vector[i];
                }
            }

            // Divide the sum by the example count to get the average (the centroid).
            for (int i = 0; i < dimensions; i++)
            {
                centroid[i] /= examples.Length;
            }

            centroids[sentiment] = centroid;
        }

        return centroids;
    }

    // Cosine similarity formula: (A . B) / (|A| * |B|). Result is in [-1, 1]; higher means more similar.
    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dotProduct = DotProduct(a, b);
        float magnitudeA = Magnitude(a);
        float magnitudeB = Magnitude(b);

        return dotProduct / (magnitudeA * magnitudeB);
    }

    // A . B = a[0]*b[0] + a[1]*b[1] + ... + a[n]*b[n]
    private static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    // |A| = sqrt(a[0]^2 + a[1]^2 + ... + a[n]^2)
    private static float Magnitude(ReadOnlySpan<float> vector)
    {
        float sumOfSquares = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }
        return MathF.Sqrt(sumOfSquares);
    }
}
