using HarnessDemo.AgentServer.Tools;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Agents;

/// <summary>
/// Normal agent ile harness agent'ın ortak parçaları: aynı talimat, aynı sipariş araçları.
/// Karşılaştırmada tek fark agent'ın nasıl oluşturulduğu olsun diye burada toplandı.
/// </summary>
public static class OrderAgentCommon
{
    public const string Instructions = """
        Sipariş destek asistanısın. Türkçe yanıt ver.
        Sipariş bilgilerini her zaman araçlarla doğrula; araç sonucunda olmayan neden veya teslim tarihi uydurma.
        Kayıt bulunamazsa bunu açıkça söyle. Eksik bilgiyi belirt.
        Müşteriye e-posta göndermek için send_customer_email aracını kullan; bu araç kullanıcı onayı gerektirir.
        """;

    /// <summary>Her agent kendi <see cref="ChatOptions"/> örneğini alır.</summary>
    public static ChatOptions CreateChatOptions(IServiceProvider sp) => new()
    {
        Instructions = Instructions,
        Tools = sp.GetRequiredService<OrderTools>().CreateTools(),
    };
}
