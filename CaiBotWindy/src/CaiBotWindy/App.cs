using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Utils;

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

    /// <summary>指令注册表。用于「父群替子群执行指令」——需要主动构造消息上下文再分派。</summary>
    public static Windy.SDK.Command.CommandRegistry Commands { get; set; } = null!;

    /// <summary>消息钩子。用于注册全局监听（如把子群的指令活动转发到父群）。</summary>
    public static Windy.SDK.Hooks.HookRegistry Hooks { get; set; } = null!;

    /// <summary>插件是否已完成初始化（HTTP 服务与图鉴数据就绪）。</summary>
    public static bool Ready { get; set; }

    /// <summary>
    /// 把当前配置写回磁盘。运行时改了配置项（如入群审核方式）后必须调用 ——
    /// 配置只在启动时读一次，不写回的话重启就丢了。
    /// </summary>
    public static void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(WindyRuntime.BasicPath, "Config", "CaiBotWindy.json");
            JsonTool.Create<PluginConfig>(configPath).InitContent(Config).Write();
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] 保存配置失败: {ex.Message}");
        }
    }

    public static CancellationToken Shutdown { get; set; } = CancellationToken.None;
}
