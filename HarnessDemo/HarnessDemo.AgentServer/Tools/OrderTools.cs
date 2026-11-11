using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Tools;

/// <summary>
/// Sipariş destek senaryosu için sahte veri ve araçlar.
/// Her iki agent (normal ve harness) aynı araç setini kullanır; fark yalnızca çalışma altyapısındadır.
/// </summary>
public sealed class OrderTools(ILogger<OrderTools> logger)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static readonly List<Order> Orders =
    [
        new("1040", "C-01", "Ayşe Yılmaz", "ayse@example.com", "delivered", "Yurtiçi", null, 0, Today.AddDays(-20)),
        new("1041", "C-02", "Mehmet Kaya", "mehmet@example.com", "delayed", "Aras", "weather", 3, Today.AddDays(-12)),
        new("1042", "C-03", "Zeynep Demir", "zeynep@example.com", "delayed", "Yurtiçi", "transfer_center_delay", 2, Today.AddDays(-6)),
        new("1043", "C-04", "Can Öztürk", "can@example.com", "delivered", "MNG", null, 0, Today.AddDays(-9)),
        new("1044", "C-01", "Ayşe Yılmaz", "ayse@example.com", "delayed", "Yurtiçi", "transfer_center_delay", 4, Today.AddDays(-15)),
        new("1045", "C-05", "Elif Şahin", "elif@example.com", "processing", null, "stock_shortage", 5, Today.AddDays(-8)),
        new("1046", "C-06", "Burak Arslan", "burak@example.com", "delayed", "Aras", "address_issue", 1, Today.AddDays(-3)),
        new("1047", "C-03", "Zeynep Demir", "zeynep@example.com", "delayed", "MNG", "transfer_center_delay", 2, Today.AddDays(-25)),
        new("1048", "C-07", "Deniz Koç", "deniz@example.com", "delayed", "Yurtiçi", "weather", 3, Today.AddDays(-18)),
        new("1049", "C-08", "Selin Aydın", "selin@example.com", "in_transit", "Aras", null, 0, Today.AddDays(-2)),
        new("1050", "C-02", "Mehmet Kaya", "mehmet@example.com", "delayed", "Aras", "stock_shortage", 6, Today.AddDays(-28)),
    ];

    /// <summary>
    /// İki agent'ın da kullandığı araç seti. Onay aracın kendi özelliğidir: e-posta aracı
    /// <see cref="ApprovalRequiredAIFunction"/> ile sarılır, okuma araçları onaysız çalışır.
    /// </summary>
    public IList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(GetOrderStatus, "get_order_status"),
        AIFunctionFactory.Create(GetShipmentEvents, "get_shipment_events"),
        AIFunctionFactory.Create(ListDelayedOrders, "list_delayed_orders"),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SendCustomerEmail, "send_customer_email")),
    ];

    [Description("Siparişin mevcut durumunu getirir.")]
    public string GetOrderStatus([Description("Sorgulanacak sipariş numarası, örn. 1042.")] string orderId)
    {
        logger.LogInformation("🔧 get_order_status({OrderId})", orderId);

        var order = Orders.FirstOrDefault(o => o.OrderId == orderId);
        if (order is null)
        {
            return JsonSerializer.Serialize(new { error = "order_not_found", orderId });
        }

        return JsonSerializer.Serialize(new
        {
            order.OrderId,
            order.CustomerName,
            order.Status,
            order.Carrier,
            order.DelayReason,
            order.DelayDays,
            orderDate = order.OrderDate.ToString("yyyy-MM-dd"),
        });
    }

    [Description("Kargo firmasındaki hareket kayıtlarını getirir. Sipariş kargoya verilmediyse boş döner.")]
    public string GetShipmentEvents([Description("Sipariş numarası.")] string orderId)
    {
        logger.LogInformation("🔧 get_shipment_events({OrderId})", orderId);

        var order = Orders.FirstOrDefault(o => o.OrderId == orderId);
        if (order is null)
        {
            return JsonSerializer.Serialize(new { error = "order_not_found", orderId });
        }

        if (order.Carrier is null)
        {
            return JsonSerializer.Serialize(new { orderId, events = Array.Empty<object>(), note = "not_shipped_yet" });
        }

        var events = new List<object>
        {
            new { at = order.OrderDate.AddDays(1).ToString("yyyy-MM-dd"), location = "İstanbul Depo", description = "Kargoya teslim edildi" },
        };

        if (order.DelayReason == "transfer_center_delay")
        {
            events.Add(new { at = order.OrderDate.AddDays(2).ToString("yyyy-MM-dd"), location = "Ankara Transfer Merkezi", description = "Transfer merkezinde yoğunluk nedeniyle bekliyor" });
        }
        else if (order.DelayReason == "weather")
        {
            events.Add(new { at = order.OrderDate.AddDays(2).ToString("yyyy-MM-dd"), location = "Erzurum Aktarma", description = "Olumsuz hava koşulları nedeniyle sevkiyat durduruldu" });
        }
        else if (order.DelayReason == "address_issue")
        {
            events.Add(new { at = order.OrderDate.AddDays(2).ToString("yyyy-MM-dd"), location = "Dağıtım Şubesi", description = "Adres bulunamadı, alıcıya ulaşılamadı" });
        }

        if (order.Status == "delivered")
        {
            events.Add(new { at = order.OrderDate.AddDays(3).ToString("yyyy-MM-dd"), location = "Alıcı Adresi", description = "Teslim edildi" });
        }

        return JsonSerializer.Serialize(new { orderId, order.Carrier, events });
    }

    [Description("Son N gün içinde verilmiş ve gecikmiş (delayed veya stok nedeniyle bekleyen) siparişleri listeler.")]
    public string ListDelayedOrders([Description("Kaç gün geriye bakılacağı, örn. 30.")] int days)
    {
        logger.LogInformation("🔧 list_delayed_orders({Days})", days);

        var since = Today.AddDays(-days);
        var delayed = Orders
            .Where(o => o.OrderDate >= since && o.DelayDays > 0)
            .Select(o => new { o.OrderId, o.CustomerId, o.CustomerName })
            .ToList();

        // Bilinçli olarak sadece kimlik bilgisi döner: agent her sipariş için ayrıca sorgu yapmak zorunda kalır.
        return JsonSerializer.Serialize(new { since = since.ToString("yyyy-MM-dd"), count = delayed.Count, orders = delayed });
    }

    [Description("Müşteriye e-posta gönderir. Bu işlem geri alınamaz.")]
    public string SendCustomerEmail(
        [Description("İlgili sipariş numarası.")] string orderId,
        [Description("E-posta konusu.")] string subject,
        [Description("E-posta gövdesi.")] string body)
    {
        var order = Orders.FirstOrDefault(o => o.OrderId == orderId);
        if (order is null)
        {
            return JsonSerializer.Serialize(new { error = "order_not_found", orderId });
        }

        logger.LogWarning("📧 send_customer_email → {Email} | {Subject}", order.Email, subject);
        return JsonSerializer.Serialize(new { sent = true, to = order.Email, subject, messageId = Guid.NewGuid().ToString("N")[..8] });
    }

    private sealed record Order(
        string OrderId,
        string CustomerId,
        string CustomerName,
        string Email,
        string Status,
        string? Carrier,
        string? DelayReason,
        int DelayDays,
        DateOnly OrderDate);
}
