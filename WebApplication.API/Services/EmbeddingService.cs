using System.Globalization;
using Microsoft.Extensions.AI;

namespace WebApplication.API.Services;

public class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var embedding = await embeddingGenerator.GenerateAsync(text);
        return embedding.Vector.ToArray();
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        var textList = texts.ToList();
        var result = new List<float[]>();

        // Process in batches of 20 to avoid API limits
        const int batchSize = 20;
        for (var i = 0; i < textList.Count; i += batchSize)
        {
            var batch = textList.Skip(i).Take(batchSize).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(batch);

            foreach (var embedding in embeddings)
            {
                result.Add(embedding.Vector.ToArray());
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a float array to SQL Server VECTOR literal format: '[0.1,0.2,...]'
    /// </summary>
    public static string ToVectorLiteral(float[] vector)
    {
        var values = string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        return $"[{values}]";
    }
}
