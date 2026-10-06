using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;
using Windy.SDK.Adaptor;
using Windy.SDK.Adaptor.QQOfficial;
using Windy.SDK.Command;

namespace CaiBotWindy.Commands;

/// <summary>命令实现共用的小工具。</summary>
internal static class CommandHelpers
{
    /// <summary>
    /// 统一的指令回复入口。
    ///
    /// <para><b>默认就是普通 Markdown</b>，只做两件附加的事：顶部 @ 提问者、多服务器时补序号提示。</para>
    ///
    /// <para><paramref name="code"/>=true 时正文进代码块（<see cref="MenuKit.ToCodeCard"/>），
    /// <b>只给插件列表 / 日志这类超长清单用</b> —— 等宽排版能对齐、又不会挤乱。
    /// 曾经把所有输出都套上「卡片头 + 分隔线」，观感太重，已回退。</para>
    /// </summary>
    public static Task ReplyAsync(
        CommandArgs args,
        string markdown,
        ButtonKeyboard? keyboard = null,
        bool code = false)
    {
        // 回复顶部先 @ 一下发指令的人：QQ 会把它渲染成头像 + 昵称，
        // 群里指令一多就能一眼看出这条是回给谁的。
        string mention = BuildMention(args);
        string bodyText = code ? MenuKit.ToCodeCard(markdown) : markdown;
        string body = string.IsNullOrEmpty(mention) ? bodyText : $"{mention}\n{bodyText}";

        // 多服务器时补一句「命令 + 序号」的用法，省得用户不知道后面能跟数字。
        string hint = BuildServerHint(args);
        if (!string.IsNullOrEmpty(hint))
        {
            body += "\n" + hint;
        }

        MessageContent content = new MessageContent().AddMarkdown(body);
        if (keyboard is not null)
        {
            content.AddButton(keyboard);
        }

        return args.Adaptor.SendMessage(content);
    }

    /// <summary>会用到服务器序号的指令 —— 只有这些的回复末尾才追加序号提示。</summary>
    private static readonly HashSet<string> ServerScopedCommands =
    [
        "在线", "在线总览", "查背包", "查看地图", "下载地图", "下载小地图",
        "排行", "插件列表", "系统状态", "远程指令", "进度查询",
    ];

    /// <summary>
    /// 本群绑定了多台服务器时，列出 `/命令 1`、`/命令 2` 这样的写法。
    /// 只有一台服务器就不打扰（提示反而占篇幅）。
    /// </summary>
    private static string BuildServerHint(CommandArgs args)
    {
        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId) || !ServerScopedCommands.Contains(args.CommandName))
        {
            return "";
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        return servers.Count <= 1 ? "" : MenuKit.ServerIndexHint(servers, $"/{args.CommandName}");
    }

    /// <summary>
    /// 生成 @ 标签。<c>&lt;qqbot-at-user&gt;</c> 是 QQ 官方 markdown 专有的，
    /// 换别的适配器（如 Milky）会原样显示成乱码，所以这里做适配器判断。
    /// </summary>
    private static string BuildMention(CommandArgs args)
    {
        // args.Adaptor 是消息级 API 包装（AdaptorMessageApi），类型判断要用全局的 App.Adaptor。
        if (App.Adaptor is not QQOfficialAdaptor)
        {
            return "";
        }

        string authorId = args.Message.AuthorId;
        return string.IsNullOrEmpty(authorId) ? "" : QQOfficialLabel.At(authorId);
    }

    /// <summary>解析可选的「服务器序号」参数；缺省或非法时返回 0（表示自动选择）。</summary>
    public static int ParseServerIndex(CommandArgs args, int index)
    {
        return args.TryGetInt(index, out int value) && value > 0 ? value : 0;
    }

    /// <summary>
    /// 解析指令目标服务器。解析失败时已回复提示消息，调用方直接 return 即可。
    /// </summary>
    public static async Task<ServerSession?> ResolveServerAsync(CommandArgs args, int index)
    {
        if (!App.Ready)
        {
            await ReplyAsync(args, "# ⏳ 机器人尚未就绪\n> HTTP 服务或图鉴数据还在初始化，请稍后再试。");
            return null;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return null;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        if (servers.Count == 0)
        {
            await ReplyAsync(args,
                "# ⛔ 本群还没有绑定服务器\n" +
                "> 在服务器控制台查看绑定码，然后发送：\n" +
                "> <qqbot-cmd-input text=\"%2F添加服务器 \" show=\"添加服务器\" reference=\"false\" />");
            return null;
        }

        if (!App.Hub.TryResolve(groupOpenId, index, out ServerSession session, out ServerRecord record))
        {
            if (index > 0)
            {
                await ReplyAsync(args, $"# ⛔ 序号 {index} 对应的服务器不存在或未连接\n> 用「服务器列表」查看当前绑定情况。");
            }
            else
            {
                await ReplyAsync(args,
                    $"# ⛔ 服务器 **{record.ServerName}** 当前离线\n" +
                    "> 请确认服务端已启动且适配插件已连接成功。");
            }

            return null;
        }

        return session;
    }

    /// <summary>发起 RPC 并统一处理超时 / 服务端 error 包。</summary>
    public static async Task<BotPacket?> RequestAsync(
        CommandArgs args,
        ServerSession session,
        PackageType type,
        JObject? payload,
        bool fileRequest = false)
    {
        try
        {
            return fileRequest
                ? await session.RequestFileAsync(type, payload, CancellationToken.None)
                : await session.RequestAsync(type, payload, CancellationToken.None);
        }
        catch (ServerRpcException ex)
        {
            string title = ex.IsTimeout ? "⏰ 服务器响应超时" : "⚠️ 服务器返回错误";
            await ReplyAsync(args, $"# {title}\n> {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            await ReplyAsync(args, $"# ⚠️ 调用服务器失败\n> {ex.Message}");
            return null;
        }
    }

    /// <summary>把「玩家名 + 可选序号」这类参数里的序号取出来（默认取最后一个参数）。</summary>
    public static int ParseTrailingIndex(CommandArgs args, int requiredCount)
    {
        if (args.Parameters.Length > requiredCount && args.TryGetInt(args.Parameters.Length - 1, out int value) && value > 0)
        {
            return value;
        }

        return 0;
    }

    /// <summary>把用不到的末尾序号参数剔除后的玩家名。</summary>
    public static string PlayerNameArgument(CommandArgs args, int index = 0)
    {
        return args.GetOrDefault(index);
    }
}
