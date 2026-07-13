using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.SqlTypes;

namespace WebApplication.API.Data.Entities;

public class DocumentChunk
{
    public int Id { get; set; }

    public int ChunkIndex { get; set; }
    public string Content { get; set; } = default!;
    public int PageNumber { get; set; }

    [Column(TypeName = "vector(1536)")]
    public SqlVector<float>? Embedding { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = default!;
}
