using System.Text;
using CaiBotWindy.Data;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>服务器查询类指令，1:1 覆盖 CaiBotLite 的数据包接口。</summary>
public static class ServerCommands
{
    private static readonly ButtonKeyboard ServerKeyboard = MenuKit.Keyboard(
        ("在线", "/在线"),
        ("进度查询", "/进度查询"),
        ("服务器列表", "/服务器列表"),
        ("查看地图", "/查看地图"),
        ("下载地图", "/下载地图"),
        ("下载小地图", "/下载小地图"),
        ("排行", "/排行"),
        ("插件列表", "/插件列表"),
        ("帮助", "/帮助"));

    // ── 在线列表（player_list）──────────────────────────────────────────────────

    [Command("在线", "查看服务器在线玩家", MessageScene.Group)]
    [Command("在线", "查看服务器在线玩家", MessageScene.GroupAt)]
    public static async Task OnlineAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.PlayerList, new JObject());
        if (packet is null)
        {
            return;
        }

        JObject payload = packet.Payload;
        string markdown = MenuKit.RenderPlayerList(
            session.GroupOpenId,
            new ServerRecordView(session.Record.PluginVersion, session.Record.EnableWhitelist),
            payload.GetString("server_name", session.Record.ServerName),
            payload.GetStringList("player_list"),
            payload.GetInt("current_online"),
            payload.GetInt("max_online"),
            payload.GetString("process"));

        await CommandHelpers.ReplyAsync(args, markdown,
            MenuKit.Keyboard(("刷新", "/在线"), ("进度查询", "/进度查询"), ("查背包", "/查背包 "), ("菜单", "/菜单")));
    }

    // ── 世界进度（progress）─────────────────────────────────────────────────────

    [Command("进度查询", "查询世界进度", MessageScene.Group)]
    [Command("进度查询", "查询世界进度", MessageScene.GroupAt)]
    public static async Task ProgressAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.Progress, new JObject());
        if (packet is null)
        {
            return;
        }

        JObject payload = packet.Payload;

        // 服务端支持文本模式（is_text = true）时直接透传。
        if (payload.GetBool("is_text"))
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 世界进度\n{payload.GetString("text")}");
            return;
        }

        // 官方 CaiBotLite 是把进度画成一张卡片发的，这里保持一致；素材缺失时退回文本列表。
        ProgressSnapshot snapshot = new()
        {
            WorldName = payload.GetString("world_name", session.Record.ServerName),
            WorldIcon = payload.GetString("world_icon"),
            DrunkWorld = payload.GetBool("drunk_world"),
            ZenithWorld = payload.GetBool("zenith_world"),
            Process = payload.GetBoolMap("process"),
            KillCounts = payload.GetIntMap("kill_counts"),
            BossLock = payload.GetStringMap("boss_lock"),
        };

        byte[]? card = ProgressRenderer.Render(snapshot);
        if (card is not null)
        {
            // msg_type=7：富媒体（图片）+ 按钮。适配器在图片分支里同时支持 keyboard。
            MessageContent message = new MessageContent()
                .AddImage(card, "progress.png", "image/png")
                .AddButton(MenuKit.Keyboard(("刷新", "/进度查询"), ("在线", "/在线"), ("菜单", "/菜单")));
            await args.Adaptor.SendMessage(message);
            return;
        }

        if (!progressCardWarningLogged)
        {
            progressCardWarningLogged = true;
            Message.Yellow($"[进度查询] 进度卡片不可用，已退回文本输出：{RenderKit.UnavailableReason ?? "未知原因"}");
        }

        string markdown = MenuKit.RenderProgress(
            snapshot.WorldName,
            snapshot.DrunkWorld,
            snapshot.ZenithWorld,
            snapshot.WorldIcon,
            snapshot.Process,
            snapshot.KillCounts,
            snapshot.BossLock);

        await CommandHelpers.ReplyAsync(args, markdown,
            MenuKit.Keyboard(("刷新", "/进度查询"), ("在线", "/在线"), ("排行", "/排行"), ("菜单", "/菜单")));
    }

    private static bool progressCardWarningLogged;

    // ── 查背包（look_bag）───────────────────────────────────────────────────────

    [Command("查背包", "查询玩家背包", MessageScene.Group)]
    [Command("查背包", "查询玩家背包", MessageScene.GroupAt)]
    public static async Task LookBagAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 查背包\n> 用法：`/查背包 <玩家名> [服务器序号]`");
            return;
        }

        string playerName = args.GetOrDefault(0);
        int index = args.Parameters.Length >= 2 ? CommandHelpers.ParseServerIndex(args, 1) : 0;

        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        JObject request = new() { ["player_name"] = playerName };
        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.LookBag, request);
        if (packet is null)
        {
            return;
        }

        JObject payload = packet.Payload;
        if (!payload.GetExists() || payload.GetInt("exist") == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 查背包\n> 没有找到玩家 **{playerName}** 的数据。");
            return;
        }

        if (payload.GetBool("is_text"))
        {
            await CommandHelpers.ReplyAsync(args, $"# 🍥 背包 · {playerName}\n{payload.GetString("text")}");
            return;
        }

        // 官方 CaiBotLite 是把背包画成一张卡片发的（services/lookbag.py）；这里同样出图，
        // 素材 / 字体缺失时退回原来的文本列表（MenuKit.RenderLookBag）。
        byte[]? card = BagRenderer.Render(BagRenderer.FromJson(payload.ToString()));
        if (card is not null)
        {
            MessageContent message = new MessageContent()
                .AddImage(card, "bag.png", "image/png")
                .AddButton(MenuKit.Keyboard(("在线", "/在线"), ("进度查询", "/进度查询"), ("菜单", "/菜单")));
            await args.Adaptor.SendMessage(message);
            return;
        }

        if (!bagCardWarningLogged)
        {
            bagCardWarningLogged = true;
            Message.Yellow($"[查背包] 背包卡片不可用，已退回文本输出：{RenderKit.UnavailableReason ?? "未知原因"}");
        }

        await CommandHelpers.ReplyAsync(args, MenuKit.RenderLookBag(payload),
            MenuKit.Keyboard(("在线", "/在线"), ("进度查询", "/进度查询"), ("菜单", "/菜单")));
    }

    private static bool bagCardWarningLogged;

    // ── 地图 / 文件 ─────────────────────────────────────────────────────────────

    [Command("查看地图", "获取世界地图预览图", MessageScene.Group)]
    [Command("查看地图", "获取世界地图预览图", MessageScene.GroupAt)]
    public static async Task MapImageAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.MapImage, new JObject(), fileRequest: true);
        if (packet is null)
        {
            return;
        }

        byte[] image;
        try
        {
            image = BinaryCodec.Decode(packet.Payload.GetString("base64"));
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⚠️ 地图数据解码失败\n> {ex.Message}");
            return;
        }

        if (image.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⚠️ 服务端返回了空的地图数据");
            return;
        }

        await args.Adaptor.SendMessage(new Windy.SDK.Adaptor.MessageContent().AddText(
            $"【{session.Record.ServerName}】世界地图（{image.Length / 1024} KB）"));
        await args.Adaptor.SendImage(image, "map.png");
    }

    [Command("下载地图", "下载世界文件", MessageScene.Group)]
    [Command("下载地图", "下载世界文件", MessageScene.GroupAt)]
    public static async Task WorldFileAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.WorldFile, new JObject(), fileRequest: true);
        if (packet is null)
        {
            return;
        }

        byte[] data;
        try
        {
            data = BinaryCodec.Decode(packet.Payload.GetString("base64"));
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⚠️ 世界文件解码失败\n> {ex.Message}");
            return;
        }

        string fileName = packet.Payload.GetString("name", "world.wld");
        await args.Adaptor.SendFile(data, fileName, "application/octet-stream");
    }

    [Command("下载小地图", "下载小地图文件", MessageScene.Group)]
    [Command("下载小地图", "下载小地图文件", MessageScene.GroupAt)]
    public static async Task MapFileAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.MapFile, new JObject(), fileRequest: true);
        if (packet is null)
        {
            return;
        }

        byte[] data;
        try
        {
            data = BinaryCodec.Decode(packet.Payload.GetString("base64"));
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⚠️ 小地图数据解码失败\n> {ex.Message}");
            return;
        }

        string fileName = packet.Payload.GetString("name", "map.tmap");
        await args.Adaptor.SendFile(data, fileName, "application/octet-stream");
    }

    // ── 排行榜（rank_data）──────────────────────────────────────────────────────

    [Command("排行", "查询服务器排行榜", MessageScene.Group)]
    [Command("排行", "查询服务器排行榜", MessageScene.GroupAt)]
    public static async Task RankAsync(CommandArgs args)
    {
        string rankType = args.GetOrDefault(0);
        string rankArg = args.Parameters.Length >= 2 && !int.TryParse(args.GetOrDefault(1), out _)
            ? args.GetOrDefault(1)
            : "";
        int index = args.Parameters.Length >= 2 ? CommandHelpers.ParseTrailingIndex(args, 1) : 0;

        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        JObject request = new()
        {
            ["rank_type"] = rankType,
            ["arg"] = rankArg,
        };

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.RankData, request);
        if (packet is null)
        {
            return;
        }

        JObject payload = packet.Payload;

        if (!payload.GetBool("rank_type_support"))
        {
            List<string> supported = payload.GetStringList("support_rank_types");
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 排行榜\n> 服务器不支持该排行类型。\n" +
                $"> 支持：{string.Join(" / ", supported)}\n" +
                "> 用法：`/排行 <类型> [参数]`",
                MenuKit.Keyboard(("BOSS 排行", "/排行 boss "), ("死亡排行", "/排行 死亡"),
                    ("在线排行", "/排行 在线"), ("钓鱼排行", "/排行 钓鱼")));
            return;
        }

        if (payload.GetBool("need_arg") && !payload.GetBool("arg_support"))
        {
            StringBuilder builder = new();
            builder.Append("# 🍥 排行榜\n");
            string message = payload.GetString("message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                builder.Append($"> {message}\n");
            }

            List<string> argsList = payload.GetStringList("support_args");
            if (argsList.Count > 0)
            {
                builder.Append($"\n{string.Join('\n', argsList.Select(item => $"- {item}"))}");
            }

            await CommandHelpers.ReplyAsync(args, builder.ToString());
            return;
        }

        (string title, Dictionary<string, string> lines) = payload.GetRank();
        await CommandHelpers.ReplyAsync(args, MenuKit.RenderRank(title, lines),
            MenuKit.Keyboard(("在线", "/在线"), ("进度查询", "/进度查询"), ("菜单", "/菜单")));
    }

    // ── 插件列表（plugin_list）──────────────────────────────────────────────────

    [Command("插件列表", "查看服务器插件 / 模组", MessageScene.Group)]
    [Command("插件列表", "查看服务器插件 / 模组", MessageScene.GroupAt)]
    public static async Task PluginListAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.PluginList, new JObject());
        if (packet is null)
        {
            return;
        }

        JObject payload = packet.Payload;
        await CommandHelpers.ReplyAsync(args,
            MenuKit.RenderPluginList(payload.GetPluginList(), payload.GetBool("is_mod")));
    }

    // ── 服务器列表 / 详情 ───────────────────────────────────────────────────────

    [Command("服务器列表", "查看本群绑定的服务器", MessageScene.Group)]
    [Command("服务器列表", "查看本群绑定的服务器", MessageScene.GroupAt)]
    public static async Task ServerListAsync(CommandArgs args)
    {
        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        await CommandHelpers.ReplyAsync(args,
            MenuKit.RenderServerList(servers, token => App.Hub.FindByToken(token)?.IsOpen == true),
            ServerKeyboard);
    }

    [Command("服务器信息", "查看服务器详细信息", MessageScene.Group)]
    [Command("服务器信息", "查看服务器详细信息", MessageScene.GroupAt)]
    public static async Task ServerInfoAsync(CommandArgs args)
    {
        int index = CommandHelpers.ParseServerIndex(args, 0);
        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        ServerRecord? target = index > 0
            ? servers.FirstOrDefault(item => item.DisplayIndex == index)
            : servers.FirstOrDefault();

        if (target is null)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 没有找到对应的服务器，请用「服务器列表」查看序号。");
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            MenuKit.RenderServerInfo(target, App.Hub.FindByToken(target.Token)?.IsOpen == true));
    }

    // ── 自踢（self_kick）───────────────────────────────────────────────────────

    [Command("自踢", "断开所有服务器连接", MessageScene.Group)]
    [Command("自踢", "断开所有服务器连接", MessageScene.GroupAt)]
    public static async Task SelfKickAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        List<ServerSession> sessions = App.Hub.FindAllByGroup(groupOpenId);
        if (sessions.Count == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 本群没有在线的服务器连接");
            return;
        }

        foreach (ServerSession session in sessions)
        {
            try
            {
                await session.NotifyAsync(PackageType.SelfKick, new JObject { ["name"] = args.Message.AuthorId },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Windy.SDK.Message.Yellow($"[CaiBotWindy] 发送自踢通知失败: {ex.Message}");
            }
        }

        await CommandHelpers.ReplyAsync(args, "# ✅ 已通知服务端断开连接");
    }

    // ── 远程指令（call_command）────────────────────────────────────────────────

    [Command("远程指令", "在服务器上执行指令", MessageScene.Group)]
    [Command("远程指令", "在服务器上执行指令", MessageScene.GroupAt)]
    public static async Task RemoteCommandAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 远程指令\n> 用法：`/远程指令 <指令内容> [服务器序号]`\n> 例如：`/远程指令 /time noon`");
            return;
        }

        List<string> parts = [.. args.Parameters];
        int index = 0;
        if (parts.Count >= 2 && parts[^1].Length <= 2 && int.TryParse(parts[^1], out int parsedIndex) && parsedIndex > 0)
        {
            index = parsedIndex;
            parts.RemoveAt(parts.Count - 1);
        }

        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null)
        {
            return;
        }

        string command = string.Join(' ', parts);
        JObject request = new()
        {
            ["command"] = command,
            ["group_open_id"] = session.GroupOpenId,
            ["user_open_id"] = args.Message.AuthorId,
        };

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.CallCommand, request);
        if (packet is null)
        {
            return;
        }

        List<string> output = packet.Payload.GetStringList("output");
        StringBuilder builder = new();
        builder.Append($"# 🍥 远程指令\n> `{command}`\n\n");
        builder.Append(output.Count == 0
            ? "> 指令已执行，无输出。"
            : string.Join('\n', output.Take(30).Select(line => $"> {line}")));

        await CommandHelpers.ReplyAsync(args, builder.ToString());
    }

    // ── 解绑服务器（unbind_server）─────────────────────────────────────────────

    [Command("解绑服务器", "解除与服务器的绑定", MessageScene.Group)]
    [Command("解绑服务器", "解除与服务器的绑定", MessageScene.GroupAt)]
    public static async Task UnbindServerAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        int index = CommandHelpers.ParseServerIndex(args, 0);
        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        ServerRecord? target = index > 0
            ? servers.FirstOrDefault(item => item.DisplayIndex == index)
            : servers.Count == 1 ? servers[0] : null;

        if (target is null)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 请指定要解绑的服务器序号，例如 `/解绑服务器 1`。\n> 用「服务器列表」查看序号。");
            return;
        }

        await App.Hub.UnbindAsync(target, "群管理员主动解绑");
        await CommandHelpers.ReplyAsync(args,
            $"# ✅ 已解绑服务器 **{target.ServerName}**\n> 服务端需要重新生成绑定码后才能再次绑定。");
    }
}
