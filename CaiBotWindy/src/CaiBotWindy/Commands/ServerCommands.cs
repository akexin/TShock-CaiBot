using System.Diagnostics;
using System.Runtime.InteropServices;
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

    [Command("在线", "查看服务器在线玩家", MessageScene.Group, "zx", "online", "谁在线", "在线玩家")]
    [Command("在线", "查看服务器在线玩家", MessageScene.GroupAt, "zx", "online", "谁在线", "在线玩家")]
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
            MenuKit.Keyboard(("刷新", "/在线"), ("进度查询", "/进度查询"), ("查背包", "/查背包 "), ("菜单", "/菜单")), code: true);
    }

    // ── 在线总览（跨服务器汇总）────────────────────────────────────────────────

    [Command("在线总览", "查看本群所有服务器的在线玩家", MessageScene.Group, "zxzl", "allonline", "全部在线")]
    [Command("在线总览", "查看本群所有服务器的在线玩家", MessageScene.GroupAt, "zxzl", "allonline", "全部在线")]
    public static async Task OnlineOverviewAsync(CommandArgs args)
    {
        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);
        if (servers.Count == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 本群还没有绑定服务器\n" +
                "> 在服务器控制台查看绑定码，然后发送 <qqbot-cmd-input text=\"%2F添加服务器 \" show=\"添加服务器\" reference=\"false\" />");
            return;
        }

        List<OnlineServerView> views = [];
        int totalOnline = 0;
        int totalMax = 0;

        foreach (ServerRecord record in servers)
        {
            string name = string.IsNullOrWhiteSpace(record.ServerName)
                ? $"服务器 {record.DisplayIndex}"
                : record.ServerName;

            if (!App.Hub.TryResolve(groupOpenId, record.DisplayIndex, out ServerSession session, out _))
            {
                views.Add(new OnlineServerView(name, false, 0, 0, []));
                continue;
            }

            try
            {
                BotPacket? packet = await session.RequestAsync(PackageType.PlayerList, new JObject(), CancellationToken.None);
                if (packet is null)
                {
                    views.Add(new OnlineServerView(name, false, 0, 0, []));
                    continue;
                }

                JObject payload = packet.Payload;
                int current = payload.GetInt("current_online");
                int max = payload.GetInt("max_online");

                totalOnline += current;
                totalMax += max;
                views.Add(new OnlineServerView(name, true, current, max, payload.GetStringList("player_list")));
            }
            catch (Exception ex)
            {
                // 单台服务器超时不该让整张表作废 —— 标成离线继续查下一台。
                Message.Yellow($"[在线总览] 服务器 {name} 查询失败: {ex.Message}");
                views.Add(new OnlineServerView(name, false, 0, 0, []));
            }
        }

        await CommandHelpers.ReplyAsync(args,
            MenuKit.RenderOnlineOverview(views, totalOnline, totalMax),
            MenuKit.Keyboard(("刷新", "/在线总览"), ("单服在线", "/在线"), ("服务器列表", "/服务器列表"), ("菜单", "/菜单")), code: true);
    }

    // ── 系统状态（服务器 + 本机）──────────────────────────────────────────────

    /// <summary>延迟采样次数。单次测量会被抖动带偏，多测几次取平均才有参考价值。</summary>
    private const int PingSamples = 3;

    [Command("系统状态", "查看服务器与本机运行状态", MessageScene.Group, "xtzt", "status", "机器状态", "状态查询")]
    [Command("系统状态", "查看服务器与本机运行状态", MessageScene.GroupAt, "xtzt", "status", "机器状态", "状态查询")]
    [Command("状态", "查看服务器与本机运行状态", MessageScene.Group)]
    [Command("状态", "查看服务器与本机运行状态", MessageScene.GroupAt)]
    public static async Task SystemStatusAsync(CommandArgs args)
    {
        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 该指令只能在群聊中使用");
            return;
        }

        List<ServerRecord> servers = DataStore.GetServers(groupOpenId);

        StringBuilder builder = new();
        builder.Append("# 🍥 系统状态\n");

        if (servers.Count == 0)
        {
            builder.Append("\n**『服务器』**\n- 本群还没有绑定服务器\n");
        }
        else
        {
            foreach (ServerRecord record in servers)
            {
                await AppendServerStatusAsync(builder, groupOpenId, record);
            }
        }

        AppendHostStatus(builder);

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("刷新", "/系统状态"), ("在线", "/在线"), ("服务器列表", "/服务器列表"), ("菜单", "/菜单")), code: true);
    }

    /// <summary>单个服务器的状态块：连接、版本、延迟（3 次采样）、在线人数。</summary>
    private static async Task AppendServerStatusAsync(StringBuilder builder, string groupOpenId, ServerRecord record)
    {
        string name = string.IsNullOrWhiteSpace(record.ServerName)
            ? $"服务器 {record.DisplayIndex}"
            : record.ServerName;

        builder.Append($"\n**『{name}』**\n");

        if (!App.Hub.TryResolve(groupOpenId, record.DisplayIndex, out ServerSession session, out _))
        {
            builder.Append("- 状态：⚪ 离线\n");
            return;
        }

        builder.Append("- 状态：🟢 在线\n");

        if (!string.IsNullOrEmpty(record.GameVersion))
        {
            builder.Append($"- 版本：Terraria {record.GameVersion} / {record.CoreVersion}\n");
        }

        if (!string.IsNullOrEmpty(record.PluginVersion))
        {
            builder.Append($"- 适配插件：{record.PluginVersion}" +
                           (record.EnableWhitelist ? " · 白名单开启\n" : " · 白名单关闭\n"));
        }

        // 旧版插件不认识 ping 包，跳过延迟探测而不是白等三次超时。
        if (SupportsPing(record.PluginVersion))
        {
            List<long> samples = [];
            for (int round = 0; round < PingSamples; round++)
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    BotPacket? packet = await session.RequestAsync(PackageType.Ping, new JObject(), cts.Token);
                    watch.Stop();
                    if (packet is not null)
                    {
                        samples.Add(watch.ElapsedMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    Message.Yellow($"[系统状态] {name} 第 {round + 1} 次探测失败: {ex.Message}");
                }
            }

            if (samples.Count > 0)
            {
                double avg = samples.Average();
                builder.Append($"- 延迟：**{Math.Round(avg)} ms**　{PingGrade(avg)}" +
                               $"（{samples.Min()}–{samples.Max()} ms / {samples.Count} 次）\n");
            }
            else
            {
                builder.Append("- 延迟：⚠️ 探测无响应\n");
            }
        }

        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            BotPacket? packet = await session.RequestAsync(PackageType.PlayerList, new JObject(), cts.Token);
            if (packet is not null)
            {
                builder.Append($"- 在线：**{packet.Payload.GetInt("current_online")} / {packet.Payload.GetInt("max_online")}**\n");
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[系统状态] {name} 在线人数查询失败: {ex.Message}");
        }
    }

    /// <summary>本机（机器人所在机器）的运行状态。</summary>
    private static void AppendHostStatus(StringBuilder builder)
    {
        Process process = Process.GetCurrentProcess();
        TimeSpan uptime = DateTime.Now - process.StartTime;

        builder.Append("\n**『本机』**\n");
        builder.Append($"- 运行时长：{DescribeDuration(uptime)}\n");
        builder.Append($"- 进程内存：{process.WorkingSet64 / 1024.0 / 1024.0:F1} MB\n");

        // 用累计 CPU 时间 / (运行时长 × 核心数) 算平均占用：瞬时值要两次采样，平均值更稳且无副作用。
        double cpu = process.TotalProcessorTime.TotalMilliseconds /
                     Math.Max(1, uptime.TotalMilliseconds * Environment.ProcessorCount) * 100;
        builder.Append($"- 平均 CPU：{cpu:F1}%（{Environment.ProcessorCount} 核）\n");

        if (TryGetMemory(out ulong totalMb, out ulong availMb))
        {
            builder.Append($"- 系统内存：{availMb / 1024.0:F1} GB 可用 / {totalMb / 1024.0:F1} GB\n");
        }

        try
        {
            DriveInfo drive = new(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\");
            if (drive.IsReady)
            {
                builder.Append($"- 磁盘 {drive.Name.TrimEnd('\\')}：{drive.AvailableFreeSpace / 1073741824.0:F1} GB 可用" +
                               $" / {drive.TotalSize / 1073741824.0:F1} GB\n");
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[系统状态] 磁盘信息读取失败: {ex.Message}");
        }
    }

    private static string DescribeDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} 天 {span.Hours} 小时";
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分"
            : $"{span.Minutes} 分 {span.Seconds} 秒";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>读物理内存。.NET 没有跨平台 API，这里直接调 Win32（本项目只在 Windows 跑）。</summary>
    private static bool TryGetMemory(out ulong totalMb, out ulong availMb)
    {
        totalMb = 0;
        availMb = 0;

        try
        {
            MemoryStatusEx status = new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                return false;
            }

            totalMb = status.TotalPhys / 1024 / 1024;
            availMb = status.AvailPhys / 1024 / 1024;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>适配插件是否支持 ping 包（2026.10.3 起）。</summary>
    private static bool SupportsPing(string pluginVersion)
    {
        // 版本号拿不到就放行，交给实际探测判断 —— 免得字段缺失时误报「版本过低」。
        if (!Version.TryParse(pluginVersion, out Version? version))
        {
            return true;
        }

        return version >= new Version(2026, 10, 3);
    }

    /// <summary>延迟质量评级。阈值按「玩家机器 → 本机服务器」的常见水平划分。</summary>
    private static string PingGrade(double milliseconds)
    {
        return milliseconds switch
        {
            < 50 => "🟢 优秀",
            < 100 => "🟢 良好",
            < 200 => "🟡 一般",
            < 400 => "🟠 偏高",
            _ => "🔴 很差",
        };
    }

    // ── 物品监控（远程调 TShock 侧 /cblmonitor）────────────────────────────────

    [Command("物品监控", "查看或设置背包物品监控阈值", MessageScene.Group, "wpjk", "monitor", "监控")]
    [Command("物品监控", "查看或设置背包物品监控阈值", MessageScene.GroupAt, "wpjk", "monitor", "监控")]
    public static async Task ItemMonitorAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
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

        // 参数原样拼成 TShock 控制台指令，规则只存在插件侧一份，避免两边同步的麻烦。
        string tail = string.Join(' ', parts);
        string command = "/cblmonitor" + (tail.Length > 0 ? " " + tail : "");

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

        string output = packet.Payload.GetString("output", "（服务端没有返回输出）");
        await CommandHelpers.ReplyAsync(args,
            $"# 🎒 物品监控\n{output}\n\n> 命中阈值时机器人会把事件广播到本群。\n> `add <物品ID> <阈值>` 新增　`del <物品ID>` 移除　无参数 = 列出全部");
    }

    // ── 世界进度（progress）─────────────────────────────────────────────────────

    [Command("进度查询", "查询世界进度", MessageScene.Group, "jdcx", "progress", "boss进度")]
    [Command("进度查询", "查询世界进度", MessageScene.GroupAt, "jdcx", "progress", "boss进度")]
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
            await CommandHelpers.ReplyAsync(args, $"# 🍥 世界进度\n{payload.GetString("text")}", code: true);
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
            MenuKit.Keyboard(("刷新", "/进度查询"), ("在线", "/在线"), ("排行", "/排行"), ("菜单", "/菜单")), code: true);
    }

    private static bool progressCardWarningLogged;

    // ── 查背包（look_bag）───────────────────────────────────────────────────────

    [Command("查背包", "查询玩家背包", MessageScene.Group, "cbb", "bag", "看背包", "背包查询")]
    [Command("查背包", "查询玩家背包", MessageScene.GroupAt, "cbb", "bag", "看背包", "背包查询")]
    public static async Task LookBagAsync(CommandArgs args)
    {
        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 查背包\n> 用法：" + MenuKit.CmdInput("/查背包 ", "查背包 <玩家名> [服务器序号]"));
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

    [Command("查看地图", "获取世界地图预览图", MessageScene.Group, "ckdt", "map", "世界地图")]
    [Command("查看地图", "获取世界地图预览图", MessageScene.GroupAt, "ckdt", "map", "世界地图")]
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

        // 统一走 Markdown 卡片回复（原来是纯文本 AddText），和其余指令保持同一版式。
        await CommandHelpers.ReplyAsync(args,
            $"# 🗺 {session.Record.ServerName}\n> 世界地图已生成（{image.Length / 1024} KB）");
        await args.Adaptor.SendImage(image, "map.png");
    }

    [Command("下载地图", "下载世界文件", MessageScene.Group, "xzdt", "dlmap", "世界文件")]
    [Command("下载地图", "下载世界文件", MessageScene.GroupAt, "xzdt", "dlmap", "世界文件")]
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

    [Command("下载小地图", "下载小地图文件", MessageScene.Group, "xzxdt", "tmap", "小地图")]
    [Command("下载小地图", "下载小地图文件", MessageScene.GroupAt, "xzxdt", "tmap", "小地图")]
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

    [Command("排行", "查询服务器排行榜", MessageScene.Group, "ph", "rank", "榜单", "排行榜")]
    [Command("排行", "查询服务器排行榜", MessageScene.GroupAt, "ph", "rank", "榜单", "排行榜")]
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
                "> 用法：" + MenuKit.CmdInput("/排行 ", "排行 <类型> [参数]"),
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

            await CommandHelpers.ReplyAsync(args, builder.ToString(), code: true);
            return;
        }

        (string title, Dictionary<string, string> lines) = payload.GetRank();
        await CommandHelpers.ReplyAsync(args, MenuKit.RenderRank(title, lines),
            MenuKit.Keyboard(("在线", "/在线"), ("进度查询", "/进度查询"), ("菜单", "/菜单")), code: true);
    }

    // ── 插件列表（plugin_list）──────────────────────────────────────────────────

    [Command("插件列表", "查看服务器插件 / 模组", MessageScene.Group, "cjlb", "plugins", "插件", "模组")]
    [Command("插件列表", "查看服务器插件 / 模组", MessageScene.GroupAt, "cjlb", "plugins", "插件", "模组")]
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
            MenuKit.RenderPluginList(payload.GetPluginList(), payload.GetBool("is_mod")), code: true);
    }

    // ── 服务器列表 / 详情 ───────────────────────────────────────────────────────

    [Command("服务器列表", "查看本群绑定的服务器", MessageScene.Group, "fwqlb", "servers", "服列表")]
    [Command("服务器列表", "查看本群绑定的服务器", MessageScene.GroupAt, "fwqlb", "servers", "服列表")]
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
            ServerKeyboard, code: true);
    }

    [Command("服务器信息", "查看服务器详细信息", MessageScene.Group, "fwqxx", "serverinfo", "服务器详情")]
    [Command("服务器信息", "查看服务器详细信息", MessageScene.GroupAt, "fwqxx", "serverinfo", "服务器详情")]
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
            MenuKit.RenderServerInfo(target, App.Hub.FindByToken(target.Token)?.IsOpen == true), code: true);
    }

    // ── 自踢（self_kick）───────────────────────────────────────────────────────

    [Command("自踢", "断开所有服务器连接", MessageScene.Group, "zt", "kick", "断开连接")]
    [Command("自踢", "断开所有服务器连接", MessageScene.GroupAt, "zt", "kick", "断开连接")]
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

    [Command("远程指令", "在服务器上执行指令", MessageScene.Group, "yczl", "rcon", "remote", "执行指令")]
    [Command("远程指令", "在服务器上执行指令", MessageScene.GroupAt, "yczl", "rcon", "remote", "执行指令")]
    public static async Task RemoteCommandAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🍥 远程指令\n> 用法：" + MenuKit.CmdInput("/远程指令 ", "远程指令 <指令内容> [服务器序号]") + "\n> 例如：`/远程指令 /time noon`");
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

    [Command("解绑服务器", "解除与服务器的绑定", MessageScene.Group, "jbfwq", "unbindserver")]
    [Command("解绑服务器", "解除与服务器的绑定", MessageScene.GroupAt, "jbfwq", "unbindserver")]
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
