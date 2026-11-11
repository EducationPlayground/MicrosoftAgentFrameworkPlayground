using HarnessDemo.AgentServer.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace HarnessDemo.AgentServer.Agents;

/// <summary>
/// Harness'in açılabilen parçalarının hepsi bir arada: varsayılanlara ek olarak web araması,
/// Docker sandbox'ta kod çalıştırma, dosya erişimi, Agent Skills ve arka plan agent'ları.
/// </summary>
/// <remarks>
/// Bilinçli olarak kapalı olanlar:
/// <list type="bullet">
/// <item>Compaction: 1.28.0'da <c>MaxContextWindowTokens</c> ile hosted session birlikte sorun çıkarıyor (bkz. README).</item>
/// <item>LoopEvaluators: döngü her turu "bitti mi?" diye yeniden çalıştırır; plan modunun soru sorması ve onay kartlarıyla
/// etkileşimli sohbette çatışır. Otonom, gözetimsiz işler için açılır.</item>
/// </list>
/// </remarks>
public static class FullHarnessAgent
{
    public const string Instructions = """
        Genel amaçlı araştırma ve kodlama asistanısın. Türkçe yanıt ver.

        Araçların:
        - web_search: güncel bilgi, sürüm, tarih ve doğrulanması gereken her iddia için. Cevapta kaynak bağlantılarını ver.
        - run_shell: Docker sandbox'ta bash komutu çalıştırır. python3 kurulu, ağ erişimi yok, her komut kullanıcı onayı ister.
          Çalışma dizini /workspace; file_access araçlarıyla yazdığın dosyalar burada görünür.
        - file_access_*: kullanıcının çalışma alanındaki dosyalar. Çıktı ve raporları buraya yaz.
        - researcher arka plan agent'ı: birbirinden bağımsız birden fazla araştırma konusu varsa her konuyu ayrı göreve ver,
          önce hepsini başlat, sonra sonuçları topla.
        - Skill'ler: göreve uyan skill varsa önce load_skill ile yükle ve kurallarına uy.

        Hesaplamayı kafadan yapma; sandbox'ta çalıştır. Bilgiyi uydurma; web_search ile doğrula.
        """;

    private const string ShellDescription =
        "Docker sandbox içinde tek bir bash komutu çalıştırır (python3 mevcut, ağ yok, /workspace yazılabilir). " +
        "stdout, stderr ve çıkış kodunu döner. Her çağrı kullanıcı onayı gerektirir.";

    /// <summary>
    /// Sandbox: python imajı, ağ kapalı, root dosya sistemi salt okunur, yetkisiz kullanıcı.
    /// Çalışma alanı klasörü /workspace'e yazılabilir bağlanır; file_access ile aynı klasördür.
    /// Stateless: komutlar aynı konteynerde ama her biri temiz bir kabukta çalışır, oturumlar arasında kabuk durumu taşınmaz.
    /// </summary>
    public static DockerShellExecutor CreateSandbox(string workspacePath)
    {
        Directory.CreateDirectory(workspacePath);

        return new DockerShellExecutor(new DockerShellExecutorOptions
        {
            Image = "python:3.13-slim",
            Mode = ShellMode.Stateless,
            HostWorkdir = workspacePath,
            MountReadonly = false,
            Network = DockerNetworkMode.None,
            Timeout = DockerShellExecutor.DefaultTimeout,
        });
    }

    public static AIAgent Create(IServiceProvider sp, string name, string contentRoot)
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var webSearch = sp.GetRequiredService<WebSearchTool>().AsAIFunction();
        var sandbox = sp.GetRequiredService<DockerShellExecutor>();

        // Arka plan agent'ı: kendi temiz oturumunda yalnızca arama yapar. Çocuğun onay istekleri
        // ana agent'a taşınmadığı için ona onay gerektiren araç verilmez.
        var researcher = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "researcher",
            Description = "Tek bir konuyu web_search ile araştırır, kaynaklı kısa özet döner.",
            ChatOptions = new ChatOptions
            {
                Instructions = "Verilen konuyu web_search ile araştır. Türkçe, en fazla 8 maddelik kaynaklı özet dön.",
                Tools = [webSearch],
            },
        });

        var todoProvider = new TodoProvider(new TodoProviderOptions());
        var modeProvider = new AgentModeProvider(new AgentModeProviderOptions());

#pragma warning disable MAAI001 // Experimental: dosya belleği, dosya erişimi, arka plan agent'ları
        AIAgent harness = chatClient.AsHarnessAgent(new HarnessAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [webSearch, sandbox.AsAIFunction("run_shell", ShellDescription, true)],
            },

            // Planlama: durumu UI'a yayınlamak için sağlayıcıları kendimiz veriyoruz.
            // ShellEnvironmentProvider sandbox'ı bir kez yoklar (kabuk, araç sürümleri) ve sonucu talimata ekler.
            // Skill'ler: varsayılan sağlayıcı load_skill için onay ister ve HarnessAgentOptions bunu açmaz.
            // skills/ klasörü bizim, script içermiyor; okumayı onaysız açıyoruz.
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            AIContextProviders =
            [
                todoProvider,
                modeProvider,
                new ShellEnvironmentProvider(sandbox, null),
                new AgentSkillsProvider(
                    new DeduplicatingAgentSkillsSource(new AgentFileSkillsSource(Path.Combine(contentRoot, "skills"), null, null, null), null),
                    new AgentSkillsProviderOptions { DisableLoadSkillApproval = true, DisableReadSkillResourceApproval = true },
                    null,
                    true),
            ],

            // Hafıza (agent'ın kendi notları) ve dosya erişimi (kullanıcının verisi) ayrı klasörler.
            FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(contentRoot, "agent-memory-full")),
            FileAccessStore = new FileSystemAgentFileStore(Path.Combine(contentRoot, "agent-workspace")),
            FileAccessProviderOptions = new FileAccessProviderOptions { DisableReadOnlyToolApproval = true }, // yazma onaylı kalır

            BackgroundAgents = [researcher],
            BackgroundAgentsProviderOptions = new BackgroundAgentsProviderOptions { WaitTimeout = TimeSpan.FromMinutes(2) },

            // Hosted web search yerine kendi web_search aracımız (bkz. WebSearchTool).
            DisableWebSearch = true,
        });
#pragma warning restore MAAI001

        return new HarnessStateAgent(harness, todoProvider, modeProvider);
    }
}
