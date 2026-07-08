using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace WebApplication.API.Services;

public class ProductTools
{
    private readonly IServiceProvider _serviceProvider;
    private IList<AITool>? _tools;

    public ProductTools(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// AI functions exposing the product catalog operations to the chat client. Built once and reused.
    /// </summary>
    public IList<AITool> Tools => _tools ??=
    [
        AIFunctionFactory.Create(SearchProductsAsync),
        AIFunctionFactory.Create(GetProductByIdAsync),
        AIFunctionFactory.Create(GetProductsByCategoryAsync),
        AIFunctionFactory.Create(GetProductsByPriceRangeAsync),
        AIFunctionFactory.Create(GetOutOftStockProductsAsync),
        AIFunctionFactory.Create(GetInStockProductsAsync),
        AIFunctionFactory.Create(GetAllCategoriesAsync)
    ];

    [Description("Belirli bir sorguyla eşleşen (Name, Description veya Category içinde arar) ürünleri arar")]
    public async Task<List<Data.Product>> SearchProductsAsync([Description("Aranacak kelime veya ifade")] string query)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        
        var q = query.ToLower();
        return await dataContext.Products
            .Where(p => p.Name.ToLower().Contains(q) || 
                        p.Description.ToLower().Contains(q) || 
                        p.Category.ToLower().Contains(q))
            .Take(10)
            .ToListAsync();
    }

    [Description("Id'ye göre tek bir ürünün detaylarını getirir")]
    public async Task<Data.Product?> GetProductByIdAsync([Description("Aramak istenen ürünün benzersiz ID'si")] int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        
        return await dataContext.Products.FindAsync(id);
    }

    [Description("Belirli bir kategori altındaki ürünleri listeler (ör. Electronics, Home, Clothing, Books)")]
    public async Task<List<Data.Product>> GetProductsByCategoryAsync([Description("Aramak istenen ürün kategorisi")] string category)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        var c = category.ToLower();
        return await dataContext.Products
            .Where(p => p.Category.ToLower() == c)
            .Take(10)
            .ToListAsync();
    }

    [Description("Belli bir fiyat aralığındaki ürünleri listeler")]
    public async Task<List<Data.Product>> GetProductsByPriceRangeAsync(
        [Description("Minimum fiyat (dahil)")] decimal minPrice, 
        [Description("Maksimum fiyat (dahil)")] decimal maxPrice)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        return await dataContext.Products
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .OrderBy(p => p.Price)
            .Take(20)
            .ToListAsync();
    }

    [Description("Stokta tükenmiş (Stock == 0) olan ürünleri listeler")]
    public async Task<List<Data.Product>> GetOutOftStockProductsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        return await dataContext.Products
            .Where(p => p.Stock == 0)
            .Take(20)
            .ToListAsync();
    }

    [Description("Stokta bulunan (Stock > 0) ürünleri listeler")]
    public async Task<List<Data.Product>> GetInStockProductsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        return await dataContext.Products
            .Where(p => p.Stock > 0)
            .Take(20)
            .ToListAsync();
    }

    [Description("Sistemde var olan tüm kategorileri distict (tekil) olarak listeler")]
    public async Task<List<string>> GetAllCategoriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

        return await dataContext.Products
            .Select(p => p.Category)
            .Distinct()
            .ToListAsync();
    }
}
