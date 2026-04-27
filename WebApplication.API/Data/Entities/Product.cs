using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.SqlTypes;

namespace WebApplication.API.Data.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;

    [Column(TypeName = "vector(1536)")]
    public SqlVector<float>? Embedding { get; set; }
}
