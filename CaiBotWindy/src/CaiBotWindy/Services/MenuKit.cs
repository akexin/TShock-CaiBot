using System.Buffers.Binary;
using System.Collections.Generic;
using CaiBotWindy.Protocol;
using Windy.SDK;
using Windy.SDK.Adaptor;

namespace CaiBotWindy.Services;

/// <summary>
/// QQ 官方 Markdown + 按钮界面的构建工具，以及各类数据包的中文渲染。
/// </summary>
/// <summary>「在线总览」里一个服务器的快照。</summary>
/// <param name="Name">服务器名（缺失时用序号兜底）。</param>
/// <param name="Online">适配插件是否已连接。</param>
/// <param name="Current">当前在线人数。</param>
/// <param name="Max">人数上限。</param>
/// <param name="Players">在线玩家名列表。</param>
public sealed record OnlineServerView(
    string Name,
    bool Online,
    int Current,
    int Max,
    IReadOnlyList<string> Players);

public static class MenuKit
{
    private static string assetRoot = "";
    private static string publicBaseUrl = "";

    public static void Configure(string assets, string publicBase)
    {
        assetRoot = assets;
        publicBaseUrl = publicBase.TrimEnd('/');
    }

    /// <summary>一组常用按钮：每行最多 3 个。</summary>
    public static ButtonKeyboard Keyboard(params (string Label, string Command)[] buttons)
    {
        ButtonKeyboard keyboard = new();
        for (int i = 0; i < buttons.Length; i += 3)
        {
            ButtonRow row = new();
            foreach ((string label, string command) in buttons.Skip(i).Take(3))
            {
                row.Buttons.Add(new MessageButton
                {
                    RenderLabel = Truncate(label, 8),
                    ActionData = command,
                });
            }

            keyboard.Rows.Add(row);
        }

        return keyboard;
    }

    /// <summary>标准功能菜单按钮组（图鉴 / 服务器 / 白名单 / 地图 / 管理）。</summary>
    public static ButtonKeyboard MainKeyboard()
    {
        return Keyboard(
            ("在线", "/在线"),
            ("进度查询", "/进度查询"),
            ("服务器列表", "/服务器列表"),
            ("搜物品", "/si "),
            ("搜生物", "/sn "),
            ("搜增益", "/sb "),
            ("搜弹幕", "/sp "),
            ("搜修饰", "/sx "),
            ("查看地图", "/查看地图"),
            ("下载地图", "/下载地图"),
            ("我的白名单", "/我的白名单"),
            ("帮助", "/帮助"));
    }

    /// <summary>图鉴检索结果过多时的候选列表。</summary>
    public static string CandidateList<T>(IReadOnlyList<T> matches, Func<T, string> name, Func<T, int> id, string command)
    {
        var lines = matches.Take(20).Select(item => $"- {name(item)}（ID：{id(item)}）");
        return string.Join('\n', lines) +
               $"\n\n> 共 {matches.Count} 条匹配，请改用 <qqbot-cmd-input text=\"%2F{command} \" show=\"{command}\" reference=\"false\" /> ID 精确查询。";
    }

    // ── 图鉴图标（目录与文件名对齐官方 CaiBotLite 的 assets/images/）────────────────

    private static readonly Dictionary<string, (int Width, int Height)> ImageSizeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>物 / 生物 / 弹幕 / 增益 的图标地址；未配置公网地址或素材缺失时返回空串。</summary>
    public static string ItemImageUrl(int itemId) => AssetUrl($"images/items/Item_{itemId}.png");

    public static string NpcImageUrl(int npcId) => AssetUrl($"images/npcs/NPC_{npcId}.png");

    public static string ProjectileImageUrl(int projectileId) => AssetUrl($"images/projectiles/Projectile_{projectileId}.png");

    public static string BuffImageUrl(int buffId) => AssetUrl($"images/buffs/Buff_{buffId}.png");

    private static string AssetUrl(string relativePath)
    {
        if (string.IsNullOrEmpty(assetRoot) || string.IsNullOrEmpty(publicBaseUrl))
        {
            return "";
        }

        string fullPath = Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return "";
        }

        return $"{publicBaseUrl}/assets/{relativePath}";
    }

    /// <summary>图鉴条目图片：按素材真实像素尺寸输出内联图（尺寸不对会被 QQ 拉伸变形）。</summary>
    public static string ImageMarkdown(string url, string relativePath, int fallback = 32)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "";
        }

        (int width, int height) = ImageSize(relativePath, fallback);
        return $"\n![text #{width}px #{height}px]({url})";
    }

    private static (int Width, int Height) ImageSize(string relativePath, int fallback)
    {
        if (ImageSizeCache.TryGetValue(relativePath, out (int Width, int Height) cached))
        {
            return cached;
        }

        (int width, int height) = (fallback, fallback);
        try
        {
            string fullPath = Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                // 直接读 PNG 的 IHDR（偏移 16/20 各 4 字节大端），避免为了取尺寸解码整张图。
                using FileStream stream = File.OpenRead(fullPath);
                Span<byte> header = stackalloc byte[24];
                if (stream.ReadAtLeast(header, 24, throwOnEndOfStream: false) == 24 &&
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                {
                    int parsedWidth = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
                    int parsedHeight = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
                    if (parsedWidth > 0 && parsedHeight > 0)
                    {
                        (width, height) = (parsedWidth, parsedHeight);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Message.Yellow($"[MenuKit] 读取图标尺寸失败 {relativePath}: {ex.Message}");
        }

        ImageSizeCache[relativePath] = (width, height);
        return (width, height);
    }

    // ── 进度 ────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> BossNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["King Slime"] = "史莱姆王",
        ["Eye of Cthulhu"] = "克苏鲁之眼",
        ["Eater of Worlds or Brain of Cthulhu"] = "世界吞噬怪 / 克苏鲁之脑",
        ["Eater of Worlds"] = "世界吞噬怪",
        ["Brain of Cthulhu"] = "克苏鲁之脑",
        ["Queen Bee"] = "蜂王",
        ["Deerclops"] = "独眼巨鹿",
        ["Skeletron"] = "骷髅王",
        ["Wall of Flesh"] = "血肉墙（困难模式）",
        ["Queen Slime"] = "史莱姆皇后",
        ["The Destroyer"] = "毁灭者",
        ["The Twins"] = "双子魔眼",
        ["Skeletron Prime"] = "机械骷髅王",
        ["Plantera"] = "世纪之花",
        ["Golem"] = "石巨人",
        ["Duke Fishron"] = "猪龙鱼公爵",
        ["Empress of Light"] = "光之女皇",
        ["Lunatic Cultist"] = "拜月教邪教徒",
        ["Tower Solar"] = "日耀柱",
        ["Tower Nebula"] = "星云柱",
        ["Tower Vortex"] = "星旋柱",
        ["Tower Stardust"] = "星尘柱",
        ["Moon Lord"] = "月亮领主",
        ["Pillars"] = "天界四柱",
        ["Goblins"] = "哥布林入侵",
        ["Pirates"] = "海盗入侵",
        ["Frost"] = "霜之军团",
        ["Frost Moon"] = "霜月",
        ["Pumpkin Moon"] = "南瓜月",
        ["Martians"] = "火星暴乱",
        ["DD2InvasionT1"] = "撒旦军队 T1",
        ["DD2InvasionT2"] = "撒旦军队 T2",
        ["DD2InvasionT3"] = "撒旦军队 T3",
    };

    public static string BossName(string key)
    {
        return BossNames.TryGetValue(key, out string? name) ? name : key;
    }

    /// <summary>渲染世界进度（<c>progress</c> 包的结构化分支）。</summary>
    public static string RenderProgress(
        string worldName,
        bool drunkWorld,
        bool zenithWorld,
        string worldIcon,
        Dictionary<string, bool> process,
        Dictionary<string, int> killCounts,
        Dictionary<string, string> bossLock)
    {
        System.Text.StringBuilder builder = new();
        builder.Append("# 🍥 世界进度\n");
        builder.Append($"- 世界：**{worldName}**");

        if (!string.IsNullOrEmpty(worldIcon))
        {
            builder.Append($"（{worldIcon}）");
        }

        if (drunkWorld)
        {
            builder.Append(" · 醉酒世界");
        }

        if (zenithWorld)
        {
            builder.Append(" · 天顶世界");
        }

        builder.Append('\n');

        int defeated = 0;
        System.Text.StringBuilder list = new();
        foreach (KeyValuePair<string, bool> item in process)
        {
            string display = BossName(item.Key);
            if (item.Value)
            {
                defeated++;
            }

            list.Append($"- {(item.Value ? "✅" : "⬜")} {display}");
            if (killCounts.TryGetValue(item.Key, out int count) && count > 0)
            {
                list.Append($"（击杀 {count} 次）");
            }

            if (bossLock.TryGetValue(item.Key, out string? lockInfo) && !string.IsNullOrWhiteSpace(lockInfo))
            {
                list.Append($" 🔒{lockInfo}");
            }

            list.Append('\n');
        }

        builder.Append($"\n**已击败 {defeated} / {process.Count}**\n\n");
        builder.Append(list);

        if (bossLock.Count > 0)
        {
            builder.Append("\n> 🔒 表示该 Boss 当前被进度锁限制\n");
        }

        return builder.ToString();
    }

    // ── 在线列表 ────────────────────────────────────────────────────────────────

    public static string RenderPlayerList(
        string groupOpenId,
        ServerRecordView server,
        string serverName,
        List<string> players,
        int currentOnline,
        int maxOnline,
        string process)
    {
        System.Text.StringBuilder builder = new();
        builder.Append("# 🍥 在线玩家\n");
        builder.Append($"- 服务器：**{serverName}**\n");
        builder.Append($"- 状态：**{currentOnline} / {maxOnline}**");
        if (server.WhitelistEnabled)
        {
            builder.Append(" · 白名单已开启");
        }

        builder.Append('\n');
        builder.Append($"- 适配插件：{server.PluginVersion}\n");

        if (!string.IsNullOrWhiteSpace(process))
        {
            builder.Append($"- 进度：{process}\n");
        }

        builder.Append('\n');
        if (players.Count == 0)
        {
            builder.Append("> 当前没有玩家在线喵~");
        }
        else
        {
            for (int i = 0; i < players.Count; i++)
            {
                builder.Append($"{i + 1}. {players[i]}\n");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 跨服务器在线总览。
    /// <para>逐个服务器发 <c>/在线</c> 在「一个群绑了好几个服」时太啰嗦，这里汇总成一张表 ——
    /// 顶部给总数，下面按服务器分组列出在线玩家。</para>
    /// </summary>
    public static string RenderOnlineOverview(
        IReadOnlyList<OnlineServerView> servers,
        int totalOnline,
        int totalMax)
    {
        System.Text.StringBuilder builder = new();
        builder.Append("# 🍥 在线总览\n");
        builder.Append($"▸ 当前在线：**{totalOnline} / {totalMax}**  ·  服务器 {servers.Count} 个\n");

        foreach (OnlineServerView server in servers)
        {
            builder.Append($"\n**『{server.Name}』**");

            if (!server.Online)
            {
                builder.Append("〔 离线 〕\n");
                continue;
            }

            builder.Append($"〔 {server.Current} / {server.Max} 〕\n");
            builder.Append(server.Players.Count == 0
                ? "> 无人在线\n"
                : string.Join(' ', server.Players.Select(name => $"[{name}]")) + "\n");
        }

        return builder.ToString();
    }

    public static string RenderServerList(IReadOnlyList<Data.ServerRecord> servers, Func<string, bool> isOnline)
    {
        if (servers.Count == 0)
        {
            return "# 🍥 服务器列表\n> 本群还没有绑定任何服务器。\n" +
                   "> 在服务器控制台查看绑定码，然后在群里发送 <qqbot-cmd-input text=\"%2F添加服务器 \" show=\"添加服务器\" reference=\"false\" />";
        }

        System.Text.StringBuilder builder = new();
        builder.Append("# 🍥 服务器列表\n");
        foreach (Data.ServerRecord server in servers)
        {
            string state = isOnline(server.Token) ? "🟢 在线" : "⚪ 离线";
            builder.Append($"\n**{server.DisplayIndex}. {server.ServerName}** — {state}\n");
            builder.Append($"- 地址：{server.Ip}:{server.Port}（{server.ServerType.DisplayName()}）\n");

            if (!string.IsNullOrEmpty(server.GameVersion))
            {
                builder.Append($"- 版本：Terraria {server.GameVersion} / {server.CoreVersion}\n");
            }

            if (!string.IsNullOrEmpty(server.PluginVersion))
            {
                builder.Append($"- 适配插件：{server.PluginVersion}");
                builder.Append(server.EnableWhitelist ? " · 白名单开启\n" : " · 白名单关闭\n");
            }
        }

        return builder.ToString();
    }

    public static string RenderServerInfo(Data.ServerRecord server, bool online)
    {
        System.Text.StringBuilder builder = new();
        builder.Append($"# 🍥 服务器信息 · {server.DisplayIndex}\n");
        builder.Append($"- 名称：**{server.ServerName}**\n");
        builder.Append($"- 连接：{(online ? "🟢 在线" : "⚪ 离线")}\n");
        builder.Append($"- 地址：{server.Ip}:{server.Port}\n");
        builder.Append($"- 类型：{server.ServerType.DisplayName()}\n");

        if (!string.IsNullOrEmpty(server.GameVersion))
        {
            builder.Append($"- Terraria：{server.GameVersion}\n");
        }

        if (!string.IsNullOrEmpty(server.CoreVersion))
        {
            builder.Append($"- 服务端核心：{server.CoreVersion}\n");
        }

        if (!string.IsNullOrEmpty(server.PluginVersion))
        {
            builder.Append($"- 适配插件：{server.PluginVersion}\n");
        }

        if (!string.IsNullOrEmpty(server.System))
        {
            builder.Append($"- 系统：{server.System}\n");
        }

        builder.Append($"- 白名单：{(server.EnableWhitelist ? "开启" : "关闭")}\n");
        builder.Append($"- 绑定时间：{server.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");

        if (server.LastSeenUtc.HasValue)
        {
            builder.Append($"\n- 最近心跳：{server.LastSeenUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        }

        return builder.ToString();
    }

    // ── 背包 ────────────────────────────────────────────────────────────────────

    public static string RenderLookBag(Newtonsoft.Json.Linq.JObject payload)
    {
        string name = payload.GetString("name");
        System.Text.StringBuilder builder = new();
        builder.Append($"# 🍥 背包 · {name}\n");
        builder.Append($"- 生命：{payload.GetString("life", "未知")}\n");
        builder.Append($"- 魔力：{payload.GetString("mana", "未知")}\n");
        builder.Append($"- 渔夫任务：{payload.GetInt("quests_completed")} 次\n");

        List<(int ItemId, int Stack)> slots = payload.GetItemSlots("inventory");
        builder.Append($"\n**物品（{slots.Count} 格）**\n");

        int shown = 0;
        foreach ((int itemId, int stack) in slots)
        {
            if (itemId <= 0 || stack <= 0)
            {
                continue;
            }

            ItemInfo? item = TerrariaData.GetItemById(itemId);
            string itemName = item?.Name is { Length: > 0 } resolved ? resolved : $"未知物品({itemId})";
            builder.Append($"- {itemName} ×{stack}\n");
            shown++;

            if (shown >= 40)
            {
                builder.Append($"> 仅显示前 40 件，实际共 {slots.Count} 格\n");
                break;
            }
        }

        if (shown == 0)
        {
            builder.Append("> 背包是空的\n");
        }

        List<int> buffs = payload.GetIntList("buffs");
        List<int> enhances = payload.GetIntList("enhances");
        if (buffs.Count > 0)
        {
            IEnumerable<string> buffNames = buffs
                .Where(id => id > 0)
                .Select(id => TerrariaData.GetBuffById(id)?.Name is { Length: > 0 } n ? n : id.ToString());
            builder.Append($"\n**增益**：{string.Join("、", buffNames)}\n");
        }

        if (enhances.Count > 0)
        {
            IEnumerable<string> enhanceNames = enhances
                .Where(id => id > 0)
                .Select(id => TerrariaData.GetItemById(id)?.Name is { Length: > 0 } n ? n : id.ToString());
            builder.Append($"**永久增益**：{string.Join("、", enhanceNames)}\n");
        }

        if (payload["economic"] is Newtonsoft.Json.Linq.JObject economic && economic.Count > 0)
        {
            builder.Append("\n**经济数据**\n");
            foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken?> item in economic)
            {
                if (item.Value is null || item.Value.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    continue;
                }

                builder.Append($"- {item.Key}：{item.Value}\n");
            }
        }

        return builder.ToString();
    }

    // ── 排行 ────────────────────────────────────────────────────────────────────

    public static string RenderRank(string title, Dictionary<string, string> lines)
    {
        if (lines.Count == 0)
        {
            return $"# 🍥 {title}\n> 暂无数据";
        }

        System.Text.StringBuilder builder = new();
        builder.Append($"# 🍥 {title}\n\n");
        int rank = 1;
        foreach (KeyValuePair<string, string> item in lines)
        {
            string medal = rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"{rank}.",
            };
            builder.Append($"{medal} {item.Key} — {item.Value}\n");
            rank++;

            if (rank > 20)
            {
                break;
            }
        }

        return builder.ToString();
    }

    // ── 插件列表 ────────────────────────────────────────────────────────────────

    public static string RenderPluginList(IReadOnlyList<Protocol.TerrariaPluginInfo> plugins, bool isMod)
    {
        string header = isMod ? "模组列表" : "插件列表";
        if (plugins.Count == 0)
        {
            return $"# 🍥 {header}\n> 服务器没有上报任何条目";
        }

        System.Text.StringBuilder builder = new();
        builder.Append($"# 🍥 {header}（{plugins.Count}）\n");
        foreach (Protocol.TerrariaPluginInfo plugin in plugins)
        {
            builder.Append($"\n**{plugin.Name}** `{plugin.Version}`\n");
            if (!string.IsNullOrEmpty(plugin.Author))
            {
                builder.Append($"- 作者：{plugin.Author}\n");
            }

            if (!string.IsNullOrEmpty(plugin.Description))
            {
                builder.Append($"- {Trim(plugin.Description, 80)}\n");
            }
        }

        return builder.ToString();
    }

    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }

    private static string Trim(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "…";
    }
}

/// <summary>渲染在线列表时需要的服务器视图字段集合。</summary>
public sealed record ServerRecordView(string PluginVersion, bool WhitelistEnabled);
