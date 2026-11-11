namespace HarnessDemo.BlazorClient.Services;

/// <summary>Sunumda iki agent'a da aynen gönderilen örnek istemler.</summary>
public static class Samples
{
    public sealed record Sample(string Label, string Prompt);

    public static IReadOnlyList<Sample> All { get; } =
    [
        new("1 · Tek sipariş", "1042 numaralı siparişim neden gecikti?"),
        new("2 · Olmayan kayıt", "9999 numaralı siparişim nerede?"),
        new("3 · Uzun görev", "Son 30 gündeki geciken siparişleri incele. Her birinin gecikme nedenini araçlarla doğrula, nedenlere göre grupla, en çok etkilenen müşterileri belirle ve destek ekibi için kısa bir rapor hazırla."),
        new("4 · Planı uygula", "Plan uygun, uygula."),
        new("5 · Onaylı e-posta", "1042 numaralı siparişin müşterisine gecikmeyi açıklayan kısa bir e-posta gönder."),
        new("6 · Hatırla", "Bundan sonra raporlarımı her zaman en fazla 5 madde ile yaz. Bunu kalıcı olarak hatırla."),
    ];

    /// <summary>Full harness sayfası: her istem bir harness parçasını öne çıkarır.</summary>
    public static IReadOnlyList<Sample> Full { get; } =
    [
        new("🌐 Web araması", ".NET'in en son yayımlanan yama sürümü hangisi ve ne zaman çıktı? Kaynak ver."),
        new("🐳 Sandbox", "Python ile 1'den 1.000.000'a kadar kaç asal sayı olduğunu hesapla. Sonucu sandbox'ta çalıştırarak bul."),
        new("📄 Dosya + kod", "Çalışma alanına fib.py adında ilk 30 Fibonacci sayısını yazdıran bir betik oluştur, sandbox'ta çalıştır ve çıktıyı fib.txt olarak kaydet."),
        new("👥 Paralel araştırma", "Microsoft Agent Framework, LangGraph ve CrewAI'ı arka plan agent'larıyla paralel araştır. teknik-rapor skill'ine uyarak bir karşılaştırma raporu hazırla ve rapor.md olarak çalışma alanına kaydet."),
        new("☑ Planı uygula", "Plan uygun, uygula."),
        new("🗂 Hatırla", "Bundan sonra kod örneklerini her zaman Python ile ver. Bunu kalıcı olarak hatırla."),
    ];
}
