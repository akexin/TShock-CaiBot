using CaiBotWindy.Data;
using CaiBotWindy.Localization;
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

        // 防御：群里不支持 <qqbot-cmd-enter>（点击即执行那个），平台会**整条拒收**：
        //   40034106 群消息不支持qqbot-cmd-enter
        // 这里统一降级成 <qqbot-cmd-input>（点击填入输入框），效果相近且各场景都支持。
        if (!string.IsNullOrEmpty(args.Message.GroupId) && body.Contains("<qqbot-cmd-enter", StringComparison.Ordinal))
        {
            body = System.Text.RegularExpressions.Regex.Replace(
                body,
                "<qqbot-cmd-enter\\s+text=\"([^\"]*)\"\\s*/>",
                "<qqbot-cmd-input text=\"$1\" reference=\"false\" />");
        }

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
        "发文件", "看文件", "存档", "存档列表", "插件打包",
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
            await ReplyAsync(args, $"# {L.Get("error.not_ready")}\n> {L.Get("hint.not_ready_body")}");
            return null;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await ReplyAsync(args, $"# {L.Get("error.group_only")}");
            return null;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        if (servers.Count == 0)
        {
            await ReplyAsync(args,
                $"# {L.Get("error.no_server")}\n" +
                $"> {L.Get("hint.bind_server")}\n" +
                "> <qqbot-cmd-input text=\"%2F添加服务器 \" show=\"添加服务器\" reference=\"false\" />");
            return null;
        }

        if (!App.Hub.TryResolve(groupOpenId, index, out ServerSession session, out ServerRecord record))
        {
            if (index > 0)
            {
                await ReplyAsync(args, $"# {L.Format("error.server_index", index)}\n> {L.Get("hint.server_list")}");
            }
            else
            {
                await ReplyAsync(args,
                    $"# {L.Format("error.server_offline", record.ServerName)}\n" +
                    $"> {L.Get("hint.server_offline_body")}");
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
            string title = ex.IsTimeout ? L.Get("error.rpc_timeout") : L.Get("error.rpc_failed");
            await ReplyAsync(args, $"# {title}\n> {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            await ReplyAsync(args, $"# {L.Get("error.call_failed")}\n> {ex.Message}");
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
