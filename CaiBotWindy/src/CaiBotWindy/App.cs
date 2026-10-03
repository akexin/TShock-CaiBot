namespace CaiBotWindy;

/// <summary>插件运行时共享状态。命令类为静态类，通过本类访问配置与各服务。</summary>
public static class App
{
    public static PluginConfig Config { get; set; } = new();

    public static Net.SessionHub Hub { get; } = new();

    public static Net.TempFileStore TempFiles { get; set; } = null!;

    public static Net.BotHttpServer Server { get; set; } = null!;

    /// <summary>插件是否已完成初始化（HTTP 服务与图鉴数据就绪）。</summary>
    public static bool Ready { get; set; }

    public static CancellationToken Shutdown { get; set; } = CancellationToken.None;
}
