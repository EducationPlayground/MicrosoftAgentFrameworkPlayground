using System.ComponentModel;
using AdvancedCustomerAgent.Data;
using AdvancedCustomerAgent.Services;

namespace AdvancedCustomerAgent;

public class CustomerAgentTools(ProductDataStore store)
{
    [Description("Ürünleri ada, açıklamaya veya kategoriye göre arar. Müşteri belirli bir ürün hakkında bilgi istediğinde kullanın.")]
    public Task<List<Product>> SearchProductsAsync(
        [Description("Aranacak metin (ürün adı, açıklama veya kategori)")] string query)
    {
        var lower = query.ToLower();
        var results = store.Products
            .Where(p => p.Name.ToLower().Contains(lower)
                     || p.Description.ToLower().Contains(lower)
                     || p.Category.ToLower().Contains(lower))
            .Take(10)
            .ToList();
        return Task.FromResult(results);
    }

    [Description("Kimliğe göre tek bir ürün getirir. Müşteri belirli bir ürün ID'si ile sorgu yaptığında kullanın.")]
    public Task<Product?> GetProductByIdAsync(
        [Description("Ürünün benzersiz kimliği (ID)")] int id)
    {
        var product = store.Products.FirstOrDefault(p => p.Id == id);
        return Task.FromResult(product);
    }

    [Description("Belirli bir kategorideki tüm ürünleri listeler. Müşteri kategori bazlı ürün sormak istediğinde kullanın.")]
    public Task<List<Product>> GetProductsByCategoryAsync(
        [Description("Filtrelenecek kategori adı")] string category)
    {
        var results = store.Products
            .Where(p => p.Category.ToLower().Contains(category.ToLower()))
            .Take(10)
            .ToList();
        return Task.FromResult(results);
    }

    [Description("Belirtilen fiyat aralığındaki ürünleri fiyata göre sıralı listeler. Müşteri belirli bir bütçeyle ürün aradığında kullanın.")]
    public Task<List<Product>> GetProductsByPriceRangeAsync(
        [Description("Minimum fiyat (TL)")] decimal minPrice,
        [Description("Maksimum fiyat (TL)")] decimal maxPrice)
    {
        var results = store.Products
            .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
            .OrderBy(p => p.Price)
            .Take(20)
            .ToList();
        return Task.FromResult(results);
    }

    [Description("Stokta olmayan (tükenmiş) ürünleri listeler. Müşteri ürün ne zaman gelecek diye sorduğunda kullanın.")]
    public Task<List<Product>> GetOutOfStockProductsAsync()
    {
        var results = store.Products
            .Where(p => p.Stock == 0)
            .Take(20)
            .ToList();
        return Task.FromResult(results);
    }

    [Description("Stokta olan ve satın alınabilecek ürünleri listeler. Müşteri hemen satın alabileceği ürünleri sorduğunda kullanın.")]
    public Task<List<Product>> GetInStockProductsAsync()
    {
        var results = store.Products
            .Where(p => p.Stock > 0)
            .Take(20)
            .ToList();
        return Task.FromResult(results);
    }

    [Description("Mağazadaki tüm ürün kategorilerini listeler. Müşteri hangi kategoriler var diye sorduğunda kullanın.")]
    public Task<List<string>> GetAllCategoriesAsync()
    {
        var categories = store.Products
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        return Task.FromResult(categories);
    }
}
