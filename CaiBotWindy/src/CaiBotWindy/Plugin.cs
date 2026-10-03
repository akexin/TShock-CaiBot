using System.Text;
using CaiBotWindy.Commands;
using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
using Windy.SDK.Events;
using Windy.SDK.Plugin;
using Windy.SDK.Utils;

namespace CaiBotWindy;

/// <summary>
/// 泰拉瑞亚服务器管理 QQ 官方机器人。
/// <para>本插件既是一套 QQ 指令（Windy 的 <c>[Command]</c>），</para>
/// <para>同时内置了一个 HTTP / WebSocket 服务端，兼容 CaiBotLite 的全部数据包接口，</para>
/// <para>供 TShock 侧的 <c>CaiBotLite</c> 适配插件主动连入。</para>
/// </summary>
public sealed class CaiBotWindyPlugin : WindyPlugin
{
    private BotHttpServer? server;

    public override string Name => "CaiBotWindy";

    public override string Version => "2026.10.3";

    public override string Author => "CaiBotWindy";

    public override string Description => "泰拉瑞亚服务器管理机器人（兼容 CaiBotLite 协议）";

    public override AdaptorType RequiredAdaptor => AdaptorType.QQOfficial;

    public override void Initialize()
    {
        // ── 1. 配置与数据 ──────────────────────────────────────────────────────
        string configPath = Path.Combine(WindyRuntime.BasicPath, "Config", "CaiBotWindy.json");
        App.Config = JsonTool.Create<PluginConfig>(configPath)
            .InitContent(new PluginConfig())
            .Read()
            .Content ?? new PluginConfig();

        string storageDirectory = ResolvePath(App.Config.StorageDirectory);
        Directory.CreateDirectory(storageDirectory);
        DataStore.Load(Path.Combine(storageDirectory, "store.json"));

        string dataDirectory = ResolvePath(App.Config.DataDirectory);
        TerrariaData.Load(dataDirectory);

        string assetDirectory = ResolvePath(App.Config.AssetDirectory);
        MenuKit.Configure(assetDirectory, App.Config.PublicBaseUrl);
        RenderKit.Configure(assetDirectory);
        if (RenderKit.UnavailableReason is string reason)
        {
            Message.Yellow($"[{Name}] 卡片渲染不可用（{reason}），进度查询 / 查背包将退回文本输出。");
        }

        // 菜单配置在 DLL 内嵌有一份：运行目录若漏拷 Menu\，此处自动补种（只补不覆盖）。
        int seededMenus = MenuSeeder.Ensure(Path.Combine(WindyRuntime.BasicPath, "Menu"));
        if (seededMenus > 0)
        {
            Message.Yellow($"[{Name}] 运行目录缺少菜单配置，已从内置资源补种 {seededMenus} 个文件到 Menu\\ 目录。");
        }

        // ── 2. HTTP / WebSocket 服务端 ─────────────────────────────────────────
        App.TempFiles = new TempFileStore(Path.Combine(storageDirectory, "temp"));
        server = new BotHttpServer(App.Config, App.Hub, App.TempFiles)
        {
            AssetRoot = assetDirectory,
            WhitelistEvaluator = (session, payload) => WhitelistService.Evaluate(session, payload),
            ServerOnline = OnServerOnline,
        };
        App.Server = server;

        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            Message.Red($"[{Name}] HTTP 服务启动失败，机器人将无法接收服务器连接: {ex.Message}");
        }

        App.Ready = true;

        // ── 3. QQ 事件 ────────────────────────────────────────────────────────
        // 存一份适配器引用：白名单校验跑在 HTTP/WebSocket 线程上，拿不到 CommandArgs，
        // 只能通过这里主动往群里推登录确认卡片。
        App.Adaptor = Adaptor;

        if (Adaptor is QQOfficialAdaptor qq)
        {
            qq.OnGroupAddRobot += OnGroupAddRobotAsync;
            qq.OnGroupDelRobot += OnGroupDelRobotAsync;
            qq.OnGroupJoinRequest += OnGroupJoinRequestAsync;
        }

        // 指令表里匹配不到时兜底回一句话，避免私聊 / 群 AT 出现「发了没反应」。
        Hooks.RegisterNoCommand(this, OnNoCommandAsync);

        Message.Green(
            $"[{Name}] v{Version} 已就绪。监听 {string.Join(", ", App.Config.ListenPrefixes)}，" +
            $"公网地址 {(string.IsNullOrEmpty(App.Config.PublicBaseUrl) ? "未配置（物品图标不可用）" : App.Config.PublicBaseUrl)}。");
        Message.Yellow(
            $"[{Name}] 下一步：在 TShock 侧安装 CaiBotLite 适配插件，并把其 BotServerUrl 指向本机器人；" +
            "然后在群里发送「/添加服务器 <IP> <端口> <绑定码>」。");
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(WindyRuntime.BasicPath, path);
    }

    /// <summary>服务端上报 hello 后通知群。</summary>
    private void OnServerOnline(ServerSession session)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Adaptor.SendMessage(
                    SendTarget.Group(session.GroupOpenId),
                    new MessageContent().AddMarkdown(
                        "# 🎉 服务器已上线\n" +
                        $"- 名称：**{session.Record.ServerName}**\n" +
                        $"- Terraria：{session.Record.GameVersion}\n" +
                        $"- 服务端：{session.Record.CoreVersion}\n" +
                        $"- 适配插件：{session.Record.PluginVersion}\n" +
                        $"- 白名单：{(session.Record.EnableWhitelist ? "开启" : "关闭")}\n\n" +
                        "👇 点下面的指令会直接填进输入框，补上参数点发送就能执行：\n" +
                        $"{MenuKit.CmdInput("/菜单")}　{MenuKit.CmdInput("/在线")}　{MenuKit.CmdInput("/进度查询")}　{MenuKit.CmdInput("/延迟")}\n" +
                        $"{MenuKit.CmdInput("/注册 ", "注册 <QQ邮箱> <角色名>")}　{MenuKit.CmdInput("/服务器列表")}　{MenuKit.CmdInput("/服务器信息")}"));
            }
            catch (Exception ex)
            {
                Message.Yellow($"[{Name}] 上线通知发送失败（可能受主动消息配额限制）: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 指令表里没有匹配项时的兜底回复。
    /// <para>群聊（未 @）保持安静；私聊与「群内 @ 但没写指令」各回一句提示，避免用户以为机器人挂了。</para>
    /// </summary>
    private static async Task OnNoCommandAsync(MessageEventArgs args)
    {
        string content = (args.Content ?? string.Empty).Trim();
        if (content.Length == 0)
        {
            return;
        }

        bool looksLikeCommand = content[0] is '/' or '!' or '！' or '／';
        if (args.Scene == MessageScene.Private)
        {
            string name = looksLikeCommand
                ? content.TrimStart('/', '!', '！', '／').Split([' ', '\u3000'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? string.Empty
                : string.Empty;

            string markdown = looksLikeCommand && name.Length > 0
                ? "# ❓ 没有这条指令\n" +
                  $"> `{name}` 不是可用指令，或只支持在群里使用。\n" +
                  "> 发送 `/菜单` 查看全部功能。"
                : "# 🍥 你好\n" +
                  "> 私聊支持：**图鉴搜索**、**白名单**、**签到**、**金币**。\n" +
                  "> 服务器相关指令请到群里 @我使用。";

            await args.Adaptor.SendMessage(new MessageContent().AddMarkdown(markdown).AddButton(HelpCommands.PrivateMenu));
            args.Handled = true;
            return;
        }

        if (args.Scene == MessageScene.GroupAt)
        {
            await args.Adaptor.SendMessage(new MessageContent().AddMarkdown(
                "# 🍥 在的\n> 不知道你想做什么喵~ 发送 `/菜单` 查看全部功能。"));
            args.Handled = true;
        }
    }

    private static async Task OnGroupAddRobotAsync(QQOfficialGroupOperationEventArgs args)
    {
        DataStore.GetOrCreateGroup(args.GroupOpenId);

        try
        {
            await args.SendToGroup(new MessageContent().AddMarkdown(
                "# 🍥 感谢邀请\n" +
                "我是泰拉瑞亚服务器管理机器人，可以帮你查询在线、进度、地图，并管理白名单。\n\n" +
                "**第一步：绑定服务器**\n" +
                "1. 在 TShock 服务器控制台查看绑定码\n" +
                "2. 在群里发送 `/添加服务器 <IP> <端口> <绑定码>`\n\n" +
                "**第二步：** 发送 `/菜单` 查看全部功能"));
        }
        catch (Exception ex)
        {
            Message.Yellow("[CaiBotWindy] 入群欢迎消息发送失败: " + ex.Message);
        }
    }

    private static Task OnGroupDelRobotAsync(QQOfficialGroupOperationEventArgs args)
    {
        Message.Yellow($"[CaiBotWindy] 机器人已被移出群 {args.GroupOpenId}，正在清理绑定。");

        foreach (ServerRecord record in DataStore.GetServers(args.GroupOpenId))
        {
            App.Hub.UnbindAsync(record, "机器人被移出群聊").GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 入群申请审核（对应 <c>GroupJoinReview = auto</c>）。
    /// <para>命中云黑（昵称或 OpenID）→ 自动拒绝并拉黑；其余自动通过。
    /// 默认只在控制台留痕、不打扰群管理员，需要群里留档就把 <c>GroupJoinNotify</c> 打开。</para>
    /// <para>入群问题、回答、平台给的风险提示都会带进通知里，方便事后追查。</para>
    /// </summary>
    private static async Task OnGroupJoinRequestAsync(QQOfficialGroupJoinRequestEventArgs args)
    {
        if (!string.Equals(App.Config.GroupJoinReview, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string who = DescribeJoinUser(args);
        bool banned = IsJoinBlacklisted(args);

        try
        {
            if (banned)
            {
                await args.RejectAsync("你在云黑名单中，如有疑问请联系群管理员", addToMemberBlacklist: true);
                Message.Yellow($"[CaiBotWindy] 已自动拒绝入群申请：{who}（云黑命中）");
            }
            else
            {
                await args.ApproveAsync();
                Message.Green($"[CaiBotWindy] 已自动通过入群申请：{who}");
            }
        }
        catch (Exception ex)
        {
            Message.Red($"[CaiBotWindy] 处理入群申请失败（{who}）: {ex.Message}");
            return;
        }

        if (App.Config.GroupJoinNotify)
        {
            await NotifyJoinResultAsync(args, banned);
        }
    }

    private static string DescribeJoinUser(QQOfficialGroupJoinRequestEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.UserName))
        {
            return args.UserName;
        }

        return string.IsNullOrEmpty(args.MemberOpenId) ? "未知用户" : args.MemberOpenId;
    }

    /// <summary>云黑判定：先查机器人级黑名单，再查本群黑名单。昵称与 OpenID 任一命中即算。</summary>
    private static bool IsJoinBlacklisted(QQOfficialGroupJoinRequestEventArgs args)
    {
        GroupRecord? global = DataStore.FindGroup(DataStore.GlobalScope);
        if (global is not null && MatchesBlacklist(global, args.UserName, args.MemberOpenId))
        {
            return true;
        }

        GroupRecord? group = DataStore.FindGroup(args.GroupOpenId);
        return group is not null && MatchesBlacklist(group, args.UserName, args.MemberOpenId);
    }

    private static bool MatchesBlacklist(GroupRecord group, string userName, string openId)
    {
        bool byName = !string.IsNullOrEmpty(userName) &&
                      group.Blacklist.Any(item => string.Equals(item, userName, StringComparison.OrdinalIgnoreCase));

        bool byOpenId = !string.IsNullOrEmpty(openId) &&
                        group.BlacklistOpenIds.Any(item => item == openId);

        return byName || byOpenId;
    }

    /// <summary>把入群申请的处理结果发到群里（可选，默认关闭）。</summary>
    private static async Task NotifyJoinResultAsync(QQOfficialGroupJoinRequestEventArgs args, bool banned)
    {
        try
        {
            StringBuilder builder = new();
            builder.Append("# 🚪 入群申请\n");
            builder.Append($"- 用户：**{DescribeJoinUser(args)}**\n");
            builder.Append($"- 结果：{(banned ? "❌ 已自动拒绝（云黑命中）" : "✅ 已自动通过")}\n");

            if (!string.IsNullOrEmpty(args.ApplySource))
            {
                builder.Append($"- 来源：{args.ApplySource}\n");
            }

            if (!string.IsNullOrEmpty(args.RiskTips))
            {
                builder.Append($"- ⚠️ 风险提示：{args.RiskTips}\n");
            }

            if (args.ReviewQuestions.Count > 0)
            {
                builder.Append('\n');
                foreach (QQOfficialJoinRequestQuestion qa in args.ReviewQuestions)
                {
                    builder.Append($"> 问题：{qa.Question}\n> 回答：{qa.Answer}\n");
                }
            }

            builder.Append("\n> 自动审核结果，如需人工复核请查看机器人控制台日志。");
            await args.SendToGroup(new MessageContent().AddMarkdown(builder.ToString()));
        }
        catch (Exception ex)
        {
            Message.Yellow($"[CaiBotWindy] 入群结果通知发送失败: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        try
        {
            server?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Message.Yellow($"[{Name}] 关闭 HTTP 服务时出错: {ex.Message}");
        }

        App.Ready = false;
        DataStore.Save();
        Message.Yellow($"[{Name}] 已卸载。");
    }
}
