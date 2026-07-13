using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using WebApplication.API.Data;

namespace WebApplication.API.Services;

public class VectorSearchService(AppDbContext db)
{
    public async Task<List<ChunkSearchResult>> SearchSimilarChunksAsync(float[] queryEmbedding, int topN = 5, int? documentId = null)
    {
        var sqlVector = new SqlVector<float>(queryEmbedding);

        return await db.DocumentChunks
            .Where(c => c.Embedding != null && (!documentId.HasValue || c.DocumentId == documentId))
            .Select(c => new
            {
                c.Id,
                c.DocumentId,
                c.Content,
                c.PageNumber,
                c.ChunkIndex,
                c.Document.OriginalFileName,
                Distance = EF.Functions.VectorDistance("cosine", c.Embedding!.Value, sqlVector)
            })
            .OrderBy(c => c.Distance)
            .Take(topN)
            .Select(c => new ChunkSearchResult(
                c.Id,
                c.DocumentId,
                c.Content,
                c.PageNumber,
                c.ChunkIndex,
                c.OriginalFileName,
                1 - c.Distance))
            .ToListAsync();
    }
}

public record ChunkSearchResult(
    int Id,
    int DocumentId,
    string Content,
    int PageNumber,
    int ChunkIndex,
    string DocumentName,
    double Relevance);

