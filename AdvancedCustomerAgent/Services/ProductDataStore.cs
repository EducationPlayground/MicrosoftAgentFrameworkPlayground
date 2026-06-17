using AdvancedCustomerAgent.Data;

namespace AdvancedCustomerAgent.Services;

public class ProductDataStore
{
    public List<Product> Products { get; } =
    [
        new Product { Id = 1, Name = "Laptop Pro 15", Description = "Güçlü işlemci ve geniş ekranlı profesyonel laptop", Category = "Bilgisayar", Price = 35000m, Stock = 12 },
        new Product { Id = 2, Name = "Kablosuz Kulaklık", Description = "Aktif gürültü önleme özellikli Bluetooth kulaklık", Category = "Ses Ekipmanları", Price = 2500m, Stock = 45 },
        new Product { Id = 3, Name = "Mekanik Klavye", Description = "RGB aydınlatmalı red switch mekanik oyun klavyesi", Category = "Çevre Birimleri", Price = 1800m, Stock = 0 },
        new Product { Id = 4, Name = "4K Monitör", Description = "27 inç 4K IPS panel monitör, 144Hz yenileme hızı", Category = "Monitör", Price = 12000m, Stock = 7 },
        new Product { Id = 5, Name = "Oyun Mouse", Description = "16000 DPI hassasiyetli optik oyun faresi", Category = "Çevre Birimleri", Price = 850m, Stock = 30 },
        new Product { Id = 6, Name = "USB-C Hub", Description = "7 portlu USB-C çoğaltıcı, HDMI ve SD kart okuyucu", Category = "Aksesuar", Price = 650m, Stock = 25 },
        new Product { Id = 7, Name = "Webcam HD", Description = "1080p Full HD web kamerası, dahili mikrofon", Category = "Kamera", Price = 1200m, Stock = 0 },
        new Product { Id = 8, Name = "Akıllı Saat", Description = "GPS ve kalp atış monitörlü spor akıllı saat", Category = "Giyilebilir", Price = 4500m, Stock = 18 },
        new Product { Id = 9, Name = "Tablet 10 inç", Description = "10 inç AMOLED ekranlı, 128GB depolama tablet", Category = "Tablet", Price = 8000m, Stock = 9 },
        new Product { Id = 10, Name = "Şarj Aleti 65W", Description = "GaN teknolojili 65W hızlı şarj adaptörü", Category = "Aksesuar", Price = 450m, Stock = 60 },
        new Product { Id = 11, Name = "SSD 1TB", Description = "NVMe PCIe 4.0 arayüzlü 1TB SSD, 7000MB/s okuma", Category = "Depolama", Price = 3200m, Stock = 22 },
        new Product { Id = 12, Name = "Grafik Kartı RTX", Description = "8GB GDDR6X bellekli oyun ve yapay zeka grafik kartı", Category = "Bilgisayar", Price = 18000m, Stock = 0 },
        new Product { Id = 13, Name = "Akıllı Hoparlör", Description = "360 derece ses dağılımlı Wi-Fi destekli akıllı hoparlör", Category = "Ses Ekipmanları", Price = 1600m, Stock = 14 },
        new Product { Id = 14, Name = "Gaming Koltuğu", Description = "Ergonomik tasarım, ayarlanabilir bel ve boyun desteği", Category = "Mobilya", Price = 7500m, Stock = 5 },
        new Product { Id = 15, Name = "Güç Kaynağı 850W", Description = "80+ Gold sertifikalı modüler ATX güç kaynağı", Category = "Bilgisayar", Price = 2800m, Stock = 11 },
    ];
}
