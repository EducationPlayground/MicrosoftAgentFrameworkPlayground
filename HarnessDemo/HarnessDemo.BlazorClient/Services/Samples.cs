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
}
