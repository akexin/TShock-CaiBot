using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using Newtonsoft.Json.Linq;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;

namespace CaiBotWindy.Commands;

/// <summary>命令实现共用的小工具。</summary>
internal static class CommandHelpers
{
    public static Task ReplyAsync(CommandArgs args, string markdown, ButtonKeyboard? keyboard = null)
    {
        MessageContent content = new MessageContent().AddMarkdown(markdown);
        if (keyboard is not null)
        {
            content.AddButton(keyboard);
        }

        return args.Adaptor.SendMessage(content);
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
