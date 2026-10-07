using System.Text;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using CaiBotWindy.Services;
using Newtonsoft.Json.Linq;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// 服务端文件下发：把运行 TShock 那台机器上的存档 / 日志 / 插件 / 任意文件发到群里。
///
/// <para><b>链路</b>：机器人发 <c>server_file</c> 请求包 → 适配插件读磁盘 → 结果按
/// 「base64 → gzip → base64」回传 → 机器人解码后用 <c>SendFile</c> 发到群。
/// 所有动作都走 <c>SendFile</c>，而 QQ 官方适配器<b>只支持往群聊发文件</b>，
/// 因此这一组指令全部限定在群内使用。</para>
///
/// <para><b>路径不做白名单</b>：能读到什么由 TShock 进程的权限决定，边界放在
/// 「仅管理员可用」这一层。这是刻意的取舍 —— 在机器人里再维护一份路径白名单，
/// 服务器换个目录结构就会静默失效，排查成本远高于它挡下的风险。</para>
///
/// <para><b>服务器序号</b>：路径里可能带空格，所以「路径 = 除末尾序号外的全部参数」，
/// 与 <c>/远程指令</c> 同一套解析习惯：末尾参数长度不超过 2 且是正整数才算序号。</para>
/// </summary>
public static class FileCommands
{
    /// <summary>
    /// 支持本功能的适配插件最低版本。取 <c>2026.10.3.2</c> 而不是 <c>2026.10.3</c>：
    /// 后者对应的构建（本地 2026.10.03.1）里还没有 <c>server_file</c>，
    /// 按前者放行会让指令干等到超时。
    /// </summary>
    private const string RequiredPluginVersion = "2026.10.3.2";

    // ── /发文件 <路径> [服务器序号] ─────────────────────────────────────────────

    [Command("发文件", "把服务器上的文件或目录发到群里（管理员）", MessageScene.Group, "fwj", "sendfile", "取文件", "下载文件")]
    [Command("发文件", "把服务器上的文件或目录发到群里（管理员）", MessageScene.GroupAt, "fwj", "sendfile", "取文件", "下载文件")]
    public static async Task SendFileAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        (string path, int index) = SplitTrailingIndex(args);
        if (path.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, SendFileUsage());
            return;
        }

        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null || !await EnsureSupportedAsync(args, session))
        {
            return;
        }

        await DownloadAndSendAsync(args, session, path);
    }

    // ── /看文件 [路径] [服务器序号] ─────────────────────────────────────────────

    [Command("看文件", "查看服务器上的目录清单或文本文件（管理员）", MessageScene.Group, "kwj", "viewfile", "目录")]
    [Command("看文件", "查看服务器上的目录清单或文本文件（管理员）", MessageScene.GroupAt, "kwj", "viewfile", "目录")]
    public static async Task ViewPathAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        (string path, int index) = SplitTrailingIndex(args);

        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null || !await EnsureSupportedAsync(args, session))
        {
            return;
        }

        // 不给路径就是「我该从哪儿找」：回报服务端那几个常用目录。
        JObject request = new() { ["action"] = path.Length == 0 ? "info" : "view" };
        if (path.Length > 0)
        {
            request["path"] = path;
        }

        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.ServerFile, request);
        if (packet is null || !await CheckOkAsync(args, packet.Payload, path))
        {
            return;
        }

        JObject payload = packet.Payload;
        await CommandHelpers.ReplyAsync(args,
            payload.GetString("action") == "info"
                ? RenderInfo(session, payload)
                : RenderView(payload),
            MenuKit.Keyboard(("看文件", "/看文件 "), ("发文件", "/发文件 "), ("菜单", "/菜单")));
    }

    // ── /存档列表 [服务器序号] ─────────────────────────────────────────────────

    [Command("存档列表", "列出服务端的世界存档文件（管理员）", MessageScene.Group, "cqlb", "worldlist")]
    [Command("存档列表", "列出服务端的世界存档文件（管理员）", MessageScene.GroupAt, "cqlb", "worldlist")]
    public static async Task WorldListAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null || !await EnsureSupportedAsync(args, session))
        {
            return;
        }

        JObject? info = await FetchInfoAsync(args, session);
        if (info is null)
        {
            return;
        }

        string worldDirectory = info.GetString("world_dir");
        if (worldDirectory.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 服务端还没有加载世界\n> 世界加载后才能列出存档目录。");
            return;
        }

        JObject request = new() { ["action"] = "view", ["path"] = worldDirectory };
        BotPacket? packet = await CommandHelpers.RequestAsync(args, session, PackageType.ServerFile, request);
        if (packet is null || !await CheckOkAsync(args, packet.Payload, worldDirectory))
        {
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            $"# 💾 世界存档\n- 目录：`{worldDirectory}`\n" +
            $"- 当前世界：`{Or(info.GetString("world_name"), "（未加载）")}`\n\n" +
            MenuKit.CodeBlock(packet.Payload.GetString("text")) + "\n" +
            "> 取回某个存档：`/存档 <文件名>`，或直接 `/发文件 <完整路径>`",
            MenuKit.Keyboard(("当前世界", "/存档"), ("看文件", "/看文件 "), ("菜单", "/菜单")));
    }

    // ── /存档 [存档名] [服务器序号] ─────────────────────────────────────────────

    [Command("存档", "把服务端的世界存档发到群里（管理员）", MessageScene.Group, "cq", "save", "worldfile")]
    [Command("存档", "把服务端的世界存档发到群里（管理员）", MessageScene.GroupAt, "cq", "save", "worldfile")]
    public static async Task WorldFileAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        (string name, int index) = SplitTrailingIndex(args);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null || !await EnsureSupportedAsync(args, session))
        {
            return;
        }

        JObject? info = await FetchInfoAsync(args, session);
        if (info is null)
        {
            return;
        }

        string worldDirectory = info.GetString("world_dir");
        if (worldDirectory.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 服务端还没有加载世界\n> 世界加载后才能取回存档。");
            return;
        }

        // 不给名字就发当前世界；给了名字则补上 .wld（带路径分隔符的按原样用）。
        if (name.Length == 0)
        {
            name = info.GetString("world_name");
            if (name.Length == 0)
            {
                await CommandHelpers.ReplyAsync(args,
                    "# ⛔ 拿不到当前世界文件名\n> 先用 `/存档列表` 看有哪些存档，再指定名字取回。");
                return;
            }
        }
        else if (!name.Contains('\\') && !name.Contains('/') && !name.EndsWith(".wld", StringComparison.OrdinalIgnoreCase))
        {
            name += ".wld";
        }

        await DownloadAndSendAsync(args, session, $"{worldDirectory.TrimEnd('/', '\\')}/{name}");
    }

    // ── /插件打包 [服务器序号] ─────────────────────────────────────────────────

    [Command("插件打包", "把服务端插件目录打包发到群里（管理员）", MessageScene.Group, "cjdb", "pluginpack")]
    [Command("插件打包", "把服务端插件目录打包发到群里（管理员）", MessageScene.GroupAt, "cjdb", "pluginpack")]
    public static async Task PluginPackAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        int index = CommandHelpers.ParseServerIndex(args, 0);
        ServerSession? session = await CommandHelpers.ResolveServerAsync(args, index);
        if (session is null || !await EnsureSupportedAsync(args, session))
        {
            return;
        }

        JObject? info = await FetchInfoAsync(args, session);
        if (info is null)
        {
            return;
        }

        string pluginDirectory = info.GetString("plugin_dir");
        if (pluginDirectory.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 服务端没有回报插件目录");
            return;
        }

        await DownloadAndSendAsync(args, session, pluginDirectory);
    }

    // ── 共用流程 ───────────────────────────────────────────────────────────────

    /// <summary>取回一个路径并作为群文件发出；服务端对目录自动打包。</summary>
    private static async Task DownloadAndSendAsync(CommandArgs args, ServerSession session, string path)
    {
        JObject request = new() { ["action"] = "download", ["path"] = path };
        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.ServerFile, request, fileRequest: true);

        if (packet is null || !await CheckOkAsync(args, packet.Payload, path))
        {
            return;
        }

        JObject payload = packet.Payload;
        byte[] data;
        try
        {
            data = BinaryCodec.Decode(payload.GetString("base64"));
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 文件解码失败\n> `{path}`\n> {ex.Message}");
            return;
        }

        if (data.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 服务端返回了空内容\n> `{path}`");
            return;
        }

        string name = payload.GetString("name", "download.bin");
        bool folder = payload.GetBool("folder");

        // 先回执再传文件：群文件上传要几秒到几十秒，没有回执用户会以为指令没反应。
        await CommandHelpers.ReplyAsync(args,
            "# 📦 服务端文件\n" +
            $"- 服务器：**{session.Record.ServerName}**\n" +
            $"- 来源：`{path}`\n" +
            $"- 内容：`{name}`　{Human(data.LongLength)}" +
            (folder ? "（目录已打包为 zip）" : ""));

        await args.Adaptor.SendFile(data, name, "application/octet-stream");
    }

    /// <summary>取服务端目录信息；失败时已回复提示。</summary>
    private static async Task<JObject?> FetchInfoAsync(CommandArgs args, ServerSession session)
    {
        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.ServerFile, new JObject { ["action"] = "info" });

        if (packet is null || !await CheckOkAsync(args, packet.Payload, ""))
        {
            return null;
        }

        return packet.Payload;
    }

    /// <summary>统一处理 <c>ok = false</c> 的业务错误。</summary>
    private static async Task<bool> CheckOkAsync(CommandArgs args, JObject payload, string path)
    {
        if (payload.GetBool("ok"))
        {
            return true;
        }

        string error = payload.GetString("error", "服务端没有说明原因");
        StringBuilder builder = new();
        builder.Append("# ⛔ 服务端拒绝了这次读取\n");
        if (path.Length > 0)
        {
            builder.Append($"- 路径：`{path}`\n");
        }

        builder.Append($"> {error}\n> 用 `/看文件 <目录>` 确认路径写法。");

        await CommandHelpers.ReplyAsync(args, builder.ToString());
        return false;
    }

    /// <summary>
    /// 适配插件不认识 <c>server_file</c> 包时不会回任何包，机器人只能干等到超时。
    /// 这里先按版本号挡掉，把 60 秒的等待换成一句明确的提示。
    /// </summary>
    private static async Task<bool> EnsureSupportedAsync(CommandArgs args, ServerSession session)
    {
        string version = session.Record.PluginVersion;
        if (!Version.TryParse(version, out Version? parsed) || parsed >= Version.Parse(RequiredPluginVersion))
        {
            return true;
        }

        await CommandHelpers.ReplyAsync(args,
            "# ⛔ 适配插件版本过低\n" +
            $"> 当前服务端适配插件为 `{version}`，文件下发需要 **{RequiredPluginVersion}** 及以上。\n" +
            "> 更新服务端的 CaiBotLite 适配插件后重试。");
        return false;
    }

    // ── 渲染 ───────────────────────────────────────────────────────────────────

    private static string RenderInfo(ServerSession session, JObject payload)
    {
        StringBuilder builder = new();
        builder.Append("# 📁 服务端目录\n");
        builder.Append($"- 服务器：**{session.Record.ServerName}**\n");
        builder.Append($"- 工作目录：`{payload.GetString("base_dir")}`\n");
        builder.Append($"- 存档目录：`{payload.GetString("world_dir")}`\n");
        builder.Append($"- 当前世界：`{Or(payload.GetString("world_name"), "（未加载）")}`\n");
        builder.Append($"- 日志目录：`{payload.GetString("log_dir")}`\n");
        builder.Append($"- 插件目录：`{payload.GetString("plugin_dir")}`\n");
        builder.Append("\n> 看目录：`/看文件 <路径>`　取回：`/发文件 <路径>`\n");
        builder.Append("> 存档：`/存档列表`、`/存档 <名字>`　插件：`/插件打包`");
        return builder.ToString();
    }

    private static string RenderView(JObject payload)
    {
        string path = payload.GetString("path");
        string name = payload.GetString("name");

        if (payload.GetString("kind") == "dir")
        {
            StringBuilder dir = new();
            dir.Append($"# 📁 {name}\n");
            dir.Append($"- 路径：`{path}`\n");
            dir.Append($"- 共 {payload.GetInt("count")} 项（目录 {payload.GetInt("dirs")} / 文件 {payload.GetInt("files")}）\n\n");
            dir.Append(MenuKit.CodeBlock(payload.GetString("text")));
            if (payload.GetBool("truncated"))
            {
                dir.Append("\n> 条目过多，只列出一部分。");
            }

            return dir.ToString();
        }

        if (payload.GetString("kind") == "binary")
        {
            return $"# 📄 {name}\n" +
                   $"- 路径：`{path}`\n" +
                   $"- 大小：{Human(SizeOf(payload))}\n\n" +
                   $"> {payload.GetString("message", "二进制文件无法文本预览。")}";
        }

        StringBuilder text = new();
        text.Append($"# 📄 {name}\n");
        text.Append($"- 路径：`{path}`\n");
        text.Append($"- 大小：{Human(SizeOf(payload))}　共 {payload.GetInt("lines")} 行");
        text.Append(payload.GetBool("truncated") ? "（预览已截断）" : "");
        text.Append("\n\n");
        text.Append(MenuKit.CodeBlock(payload.GetString("text")));
        return text.ToString();
    }

    private static long SizeOf(JObject payload)
    {
        JToken? token = payload["size"];
        return token is null || token.Type == JTokenType.Null ? 0 : token.Value<long>();
    }

    /// <summary>空串取兜底：<c>GetString</c> 的 fallback 只在键缺失或为 null 时生效，空串得自己判。</summary>
    private static string Or(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string SendFileUsage()
    {
        return "# 📦 发文件\n" +
               "> 用法：" + MenuKit.CmdInput("/发文件 ", "发文件 <路径> [服务器序号]") + "\n" +
               "> 例：`/发文件 tshock/CaiBotLite.json`、`/发文件 ServerPlugins`\n" +
               "> 目录会自动打包成 zip。\n\n" +
               "> 先看有什么：`/看文件`（列出服务端常用目录）\n" +
               "> 存档：`/存档列表`　插件：`/插件打包`";
    }

    // ── 工具 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 把参数拆成「路径 + 可选的末尾服务器序号」。
    /// 与 <c>/远程指令</c> 同一套规则：末尾参数长度不超过 2 且为正整数时才算序号 ——
    /// 路径里带空格很常见，反过来（先取序号再看路径）会切错。
    /// </summary>
    private static (string Path, int Index) SplitTrailingIndex(CommandArgs args)
    {
        List<string> parts = [.. args.Parameters];
        int index = 0;

        if (parts.Count >= 2 && parts[^1].Length <= 2 && int.TryParse(parts[^1], out int parsed) && parsed > 0)
        {
            index = parsed;
            parts.RemoveAt(parts.Count - 1);
        }

        return (string.Join(' ', parts).Trim().Trim('"'), index);
    }

    private static string Human(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
