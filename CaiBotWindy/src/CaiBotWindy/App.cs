using Windy.SDK.Adaptor;

namespace CaiBotWindy;

/// <summary>插件运行时共享状态。命令类为静态类，通过本类访问配置与各服务。</summary>
public static class App
{
    public static PluginConfig Config { get; set; } = new();

    public static Net.SessionHub Hub { get; } = new();

    public static Net.TempFileStore TempFiles { get; set; } = null!;

    public static Net.BotHttpServer Server { get; set; } = null!;

    /// <summary>
    /// 当前 QQ 适配器。指令回调里可以直接用 <c>args.Adaptor</c>，
    /// 但这个引用是给「拿不到 CommandArgs 的地方」准备的 —— 典型场景是白名单校验线程
    /// 需要主动往群里推一条登录确认消息（那里只有 groupOpenId，没有消息上下文）。
    /// </summary>
    public static Adaptor? Adaptor { get; set; }

    /// <summary>插件是否已完成初始化（HTTP 服务与图鉴数据就绪）。</summary>
    public static bool Ready { get; set; }

    public static CancellationToken Shutdown { get; set; } = CancellationToken.None;
}
