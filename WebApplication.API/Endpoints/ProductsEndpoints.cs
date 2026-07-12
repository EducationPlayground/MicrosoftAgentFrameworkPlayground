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
        // Headphones / earbuds
        "Wireless Bluetooth Headphones",
        "Noise Cancelling Earbuds",
        "Over-Ear Studio Headphones",
        "True Wireless Sports Earbuds",
        "Bluetooth Neckband Earphones",

        // Chargers / power
        "USB-C Laptop Charger 65W",
        "USB-C Fast Wall Charger 30W",
        "Wireless Charging Pad 15W",
        "Portable Power Bank 20000mAh",
        "Car USB-C Charger Dual Port",

        // Keyboards
        "Mechanical Keyboard RGB Backlit",
        "Wireless Compact Keyboard",
        "Ergonomic Split Keyboard",
        "Mini Bluetooth Keyboard for Tablets",

        // Monitors / displays
        "4K Ultra HD Monitor 27 inch",
        "Curved Gaming Monitor 32 inch",
        "Portable USB-C Monitor 15.6 inch",
        "Ultrawide Monitor 34 inch",

        // Storage
        "Portable SSD 1TB USB 3.2",
        "External Hard Drive 2TB",
        "USB Flash Drive 128GB",
        "NVMe M.2 SSD 1TB",

        // Mice
        "Gaming Mouse with DPI Control",
        "Wireless Ergonomic Mouse",
        "Vertical Mouse for Wrist Comfort",
        "Compact Travel Mouse",

        // Webcams
        "Webcam 1080p Full HD",
        "4K Webcam with Auto Focus",
        "Webcam with Privacy Cover",

        // Desk lighting
        "Smart LED Desk Lamp",
        "Adjustable LED Reading Lamp",
        "USB Rechargeable Desk Light",

        // Office furniture
        "Ergonomic Office Chair",
        "Standing Desk Converter",
        "Electric Height-Adjustable Desk",
        "Ergonomic Kneeling Chair",

        // Accessories
        "Multi-port USB Hub",
        "USB-C Docking Station",
        "Laptop Stand Adjustable Aluminum",
        "Wrist Rest Mouse Pad",
        "Smartphone Screen Protector",
        "Phone Camera Lens Kit",
        "Tablet Stylus Pen"
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

            // 3. Keyword search — lexical ranking using SQL Server full-text search (CONTAINSTABLE), most relevant first
            var rankedKeywordResults = await dbContext.Database
                .SqlQuery<KeywordSearchResult>($"""
                                                SELECT TOP (20) p.[Id], p.[Name], kt.[RANK] AS [Rank]
                                                FROM [Products] AS p
                                                INNER JOIN CONTAINSTABLE([Products], [Name], {q}) AS kt ON p.[Id] = kt.[KEY]
                                                ORDER BY kt.[RANK] DESC
                                                """)
                .ToListAsync();

            var keywordResults = rankedKeywordResults
                .Select(r => new { r.Id, r.Name })
                .ToList();

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

    // Maps the result of the CONTAINSTABLE full-text search query used in keyword ranking
    private sealed record KeywordSearchResult(int Id, string Name, int Rank);
}
