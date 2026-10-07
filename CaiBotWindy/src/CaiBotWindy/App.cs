using CaiBotWindy.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// 出站消息队列。
    ///
    /// <para><b>通知类</b>消息走它（事件广播、上线通知、子群回流、登录确认卡片），
    /// 避免同一瞬间冒出好几条发送把平台限频打满，也避免阻塞 WebSocket 读取线程。</para>
    ///
    /// <para>指令的<b>直接回复不经过队列</b> —— 用户发完指令是等着看的，必须立刻回。</para>
    /// </summary>
    public static Infrastructure.MessageQueue Outbox { get; } = new();

    /// <summary>配置文件路径（<c>Config/CaiBotWindy.json</c>）。</summary>
    public static string ConfigPath =>
        Path.Combine(WindyRuntime.BasicPath, "Config", "CaiBotWindy.json");

    /// <summary>
    /// 读取配置。文件缺失用默认值；文件损坏（手工改错 / 写入中断）自动回退到最近一份备份。
    ///
    /// <para><b>为什么要回退</b>：配置文件一旦解析失败，机器人<b>起都起不来</b>，
    /// 而此时人往往不在机器旁边。宁可用上一份能用的配置先跑起来。</para>
    /// </summary>
    public static PluginConfig LoadConfig()
    {
        string path = ConfigPath;
        string? json = AtomicFile.ReadWithFallback(path, IsUsableJson);
        if (json is null)
        {
            return new PluginConfig();
        }

        try
        {
            return JsonConvert.DeserializeObject<PluginConfig>(json) ?? new PluginConfig();
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] 配置解析失败，已回退到默认配置: {ex.Message}");
            return new PluginConfig();
        }
    }

    /// <summary>
    /// 把当前配置写回磁盘。运行时改了配置项（如入群审核方式）后必须调用 ——
    /// 配置只在启动时读一次，不写回的话重启就丢了。
    ///
    /// <para>写入是原子的：先写临时文件再替换，替换前自动备份旧配置。</para>
    /// </summary>
    public static void SaveConfig()
    {
        try
        {
            string json = JsonConvert.SerializeObject(Config, Formatting.Indented);

            // 配置里含 SMTP 授权码等敏感信息，备份份数压到 3 —— 够回滚，少留副本。
            AtomicFile.Write(ConfigPath, json, backup: true, keepBackups: 3);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] 保存配置失败: {ex.Message}");
        }
    }

    /// <summary>内容是否像一份能解析的 JSON 对象。</summary>
    private static bool IsUsableJson(string content)
    {
        try
        {
            JObject.Parse(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static CancellationToken Shutdown { get; set; } = CancellationToken.None;
}
