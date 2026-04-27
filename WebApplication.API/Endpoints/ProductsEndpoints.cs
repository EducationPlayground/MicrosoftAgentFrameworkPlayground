using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WebApplication.API.Data;
using WebApplication.API.Data.Entities;

namespace WebApplication.API.Endpoints;

public static class ProductsEndpoints
{
    private static readonly string[] SampleProducts =
    [
        "Wireless Bluetooth Headphones",
        "USB-C Laptop Charger 65W",
        "Mechanical Keyboard RGB Backlit",
        "4K Ultra HD Monitor 27 inch",
        "Portable SSD 1TB USB 3.2",
        "Gaming Mouse with DPI Control",
        "Noise Cancelling Earbuds",
        "Webcam 1080p Full HD",
        "Smart LED Desk Lamp",
        "Ergonomic Office Chair",
        "Standing Desk Converter",
        "Multi-port USB Hub",
        "Laptop Stand Adjustable Aluminum",
        "Wrist Rest Mouse Pad",
        "Smartphone Screen Protector"
    ];

    public static void MapProductEndpoints(this global::Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        // POST /products/seed — seeds sample products with their embeddings
        group.MapPost("/seed", async (
            AppDbContext dbContext,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) =>
        {
            if (await dbContext.Products.AnyAsync())
                return Results.Ok("Products already seeded.");

            var products = new List<Product>();

            foreach (var name in SampleProducts)
            {
                var result = await embeddingGenerator.GenerateAsync([name]);
                products.Add(new Product
                {
                    Name = name,
                    Embedding = new SqlVector<float>(result[0].Vector)
                });
            }

            dbContext.Products.AddRange(products);
            await dbContext.SaveChangesAsync();

            return Results.Ok($"{products.Count} products seeded.");
        })
        .WithSummary("Seeds sample products with their embeddings");

        // GET /products/search?q=... — semantic search
        group.MapGet("/search", async (
            string q,
            AppDbContext dbContext,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) =>
        {
            var queryResult = await embeddingGenerator.GenerateAsync([q]);
            var queryVector = new SqlVector<float>(queryResult[0].Vector);

            var results = await dbContext.Products
                .Where(p => p.Embedding != null)
                .OrderBy(p => EF.Functions.VectorDistance("cosine", p.Embedding!.Value, queryVector))
                .Take(5)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    Distance = EF.Functions.VectorDistance("cosine", p.Embedding!.Value, queryVector)
                })
                .ToListAsync();

            return Results.Ok(results);
        })
        .WithSummary("Performs semantic search over product names");

        // GET /products/hybrid-search?q=... — hybrid search (vector + keyword, RRF)
        group.MapGet("/hybrid-search", async (
            string q,
            AppDbContext dbContext,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) =>
        {
            // 1. Generate query embedding
            var queryResult = await embeddingGenerator.GenerateAsync([q]);
            var queryVector = new SqlVector<float>(queryResult[0].Vector);

            // 2. Vector search — semantic ranking by cosine distance
            var vectorResults = await dbContext.Products
                .Where(p => p.Embedding != null)
                .OrderBy(p => EF.Functions.VectorDistance("cosine", p.Embedding!.Value, queryVector))
                .Take(20)
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();

            // 3. Keyword search — lexical ranking using Contains (translates to LIKE '%...%')
            var keywords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var keywordResults = await dbContext.Products
                .Where(p => keywords.Any(kw => p.Name.Contains(kw)))
                .Select(p => new { p.Id, p.Name })
                .Take(20)
                .ToListAsync();

            // 4. Reciprocal Rank Fusion (RRF, k=60)
            const double k = 60.0;
            var scores = new Dictionary<int, double>();

            for (var i = 0; i < vectorResults.Count; i++)
                scores[vectorResults[i].Id] = scores.GetValueOrDefault(vectorResults[i].Id) + 1.0 / (k + i + 1);

            for (var i = 0; i < keywordResults.Count; i++)
                scores[keywordResults[i].Id] = scores.GetValueOrDefault(keywordResults[i].Id) + 1.0 / (k + i + 1);

            var nameMap = vectorResults.Concat(keywordResults)
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var results = scores
                .Select(kv => new { Id = kv.Key, Name = nameMap[kv.Key], RrfScore = kv.Value })
                .OrderByDescending(r => r.RrfScore)
                .Take(5)
                .ToList();

            return Results.Ok(results);
        })
        .WithSummary("Performs hybrid search combining vector similarity and keyword matching using RRF");
    }
}
