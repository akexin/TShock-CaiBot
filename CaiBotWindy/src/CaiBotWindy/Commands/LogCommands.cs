using System.Globalization;
using System.IO.Compression;
using System.Text;
using CaiBotWindy.Net;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// TShock 服务端日志的查看与检索。
///
/// <para><b>日志从哪来</b>：默认（<c>LogSource = auto</c>）优先用**在线服务端**——通过适配插件的
/// <c>server_file</c> 包把远端日志目录镜像到本地缓存，因此机器人可以和 TShock 不在同一台机器上。
/// 没有在线服务端时回退到本机 <c>TShockLogDirectory</c>。</para>
///
/// <para>TShock 每次启动会新开一个 <c>日期_时间.log</c>，所以这里默认读**最新的那个**。</para>
///
/// <para>分页是倒序的：**第 1 页是最新的若干行** —— 排查问题时几乎总是想看最新的。</para>
///
/// <para>五种查看方式，覆盖「快速扫一眼」到「把整份日志拿走」：
/// <list type="bullet">
///   <item><c>/日志</c> —— 分页扫最新日志；</item>
///   <item><c>/日志 区间 起 止</c> —— 只看某一段行号；</item>
///   <item><c>/日志 下载 [序号]</c> —— 把某一份完整日志作为群文件发出来；</item>
///   <item><c>/日志 打包</c> —— 全部历史日志打成一个 zip 发出来；</item>
///   <item><c>/日志 刷新</c> —— 强制重新拉取远端日志（默认按间隔自动刷新）。</item>
/// </list>
/// 要取服务端任意路径的文件，用 <c>/发文件</c>。</para>
/// </summary>
public static class LogCommands
{
    /// <summary>每页行数。QQ 单条 markdown 消息有长度上限，20 行是稳妥值。</summary>
    private const int PageSize = 20;

    /// <summary>单行最大展示长度。异常堆栈那种超长行会把整页挤爆，超了截断。</summary>
    private const int MaxLineLength = 120;

    /// <summary>「区间」一次最多展示的行数。再多就该用「下载」把整份日志拿走。</summary>
    private const int MaxSliceLines = 200;

    /// <summary>单个日志文件的发送上限。</summary>
    private const long MaxSendBytes = 100L * 1024 * 1024;

    // ── /日志 [页码 | 子命令] ──────────────────────────────────────────────────

    [Command("日志", "查看 TShock 服务器日志（分页）", MessageScene.Group, "rz", "log", "logs", "服务端日志")]
    [Command("日志", "查看 TShock 服务器日志（分页）", MessageScene.GroupAt, "rz", "log", "logs", "服务端日志")]
    public static async Task LogAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        string sub = args.GetOrDefault(0).Trim();

        // 帮助不碰任何数据来源，先挡掉。
        if (sub is "帮助" or "help" or "?")
        {
            await CommandHelpers.ReplyAsync(args, UsageText());
            return;
        }

        bool force = sub is "刷新" or "refresh" or "同步";
        LogSource? source = await ResolveSourceAsync(args, force);
        if (source is null)
        {
            return;
        }

        switch (sub)
        {
            case "文件" or "files" or "list":
                await ListFilesAsync(args, source);
                return;

            case "下载" or "发送" or "download":
                await DownloadAsync(args, source, args.GetOrDefault(1).Trim());
                return;

            case "打包" or "zip" or "全部":
                await PackAsync(args, source);
                return;

            case "区间" or "部分" or "range":
                await SliceAsync(args, source);
                return;

            case "刷新" or "refresh" or "同步":
                await ReportRefreshedAsync(args, source);
                return;
        }

        string? file = LatestLogFile(source.Directory);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 📜 服务器日志\n> 来源：{source.Label}\n> 目录里还没有日志文件：`{source.DisplayDirectory}`");
            return;
        }

        int page = int.TryParse(sub, out int parsed) && parsed > 0 ? parsed : 1;
        await RenderAsync(args, source, file, page, keyword: null);
    }

    /// <summary><c>/日志 刷新</c>：重拉已经在 <see cref="ResolveSourceAsync"/> 里做过，这里只回报结果。</summary>
    private static async Task ReportRefreshedAsync(CommandArgs args, LogSource source)
    {
        FileInfo[] files;
        try
        {
            files = LogFiles(source.Directory);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 读取目录失败\n> " + ex.Message);
            return;
        }

        StringBuilder builder = new();
        builder.Append("# 🔄 日志已刷新\n");
        builder.Append($"- 来源：{source.Label}\n");

        if (files.Length == 0)
        {
            builder.Append($"> 目录里还没有日志文件：`{source.DisplayDirectory}`");
        }
        else
        {
            builder.Append($"- 共 {files.Length} 份日志\n");
            builder.Append($"- 最新：`{files[0].Name}`　{LogTime(files[0]):yyyy-MM-dd HH:mm}");
        }

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("看最新", "/日志"), ("文件列表", "/日志 文件"), ("打包全部", "/日志 打包")));
    }

    // ── /日志搜索 <关键词> [页码] ──────────────────────────────────────────────

    [Command("日志搜索", "在 TShock 日志中搜索关键词", MessageScene.Group, "rzss", "logsearch", "搜日志")]
    [Command("日志搜索", "在 TShock 日志中搜索关键词", MessageScene.GroupAt, "rzss", "logsearch", "搜日志")]
    public static async Task SearchLogAsync(CommandArgs args)
    {
        if (!await Permissions.RequireAdminAsync(args))
        {
            return;
        }

        if (!args.Require(1))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 🔍 日志搜索\n> 用法：" +
                MenuKit.CmdInput("/日志搜索 ", "日志搜索 <关键词> [页码]") + "\n" +
                "> 例：`/日志搜索 玩家`、`/日志搜索 ERROR`、`/日志搜索 白名单`\n" +
                "> 只看最新日志：`/日志`");
            return;
        }

        LogSource? source = await ResolveSourceAsync(args, force: false);
        if (source is null)
        {
            return;
        }

        string keyword = args.GetOrDefault(0).Trim();
        int page = args.TryGetInt(1, out int parsed) && parsed > 0 ? parsed : 1;

        string? file = LatestLogFile(source.Directory);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 📜 服务器日志\n> 来源：{source.Label}\n> 目录里还没有日志文件：`{source.DisplayDirectory}`");
            return;
        }

        await RenderAsync(args, source, file, page, keyword);
    }

    // ── 渲染 ───────────────────────────────────────────────────────────────────

    private static async Task RenderAsync(CommandArgs args, LogSource source, string file, int page, string? keyword)
    {
        List<string> all;
        try
        {
            all = ReadAllLines(file);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[日志] 读取失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 读取日志失败\n> " + ex.Message + $"\n> 文件：`{file}`");
            return;
        }

        List<string> lines = string.IsNullOrEmpty(keyword)
            ? all
            : [.. all.Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))];

        if (lines.Count == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 🔍 没有匹配的日志\n> 关键词：`{keyword}`\n" +
                $"> 文件：`{Path.GetFileName(file)}`（共 {all.Count} 行）");
            return;
        }

        int totalPages = Math.Max(1, (int)Math.Ceiling(lines.Count / (double)PageSize));
        int current = Math.Clamp(page, 1, totalPages);

        // 倒序：第 1 页是最新的若干行。
        int end = lines.Count - (current - 1) * PageSize;
        int start = Math.Max(0, end - PageSize);
        List<string> slice = lines.GetRange(start, end - start);

        StringBuilder builder = new();
        builder.Append(keyword is null ? "# 📜 服务器日志\n" : $"# 🔍 日志搜索：{keyword}\n");
        builder.Append($"**{Path.GetFileName(file)}**");
        builder.Append(keyword is null ? $"　共 {lines.Count} 行　" : $"　命中 {lines.Count} 行　");
        builder.Append($"第 {current} / {totalPages} 页\n\n");

        StringBuilder content = new();
        foreach (string raw in slice)
        {
            content.Append(raw.Length > MaxLineLength ? raw[..MaxLineLength] + "…" : raw).Append('\n');
        }

        // 日志是「原样输出」的典型：放进代码块用等宽字体，时间戳能对齐，也不会被当成格式符。
        builder.Append(MenuKit.CodeBlock(content.ToString())).Append('\n');
        builder.Append(keyword is null
            ? "> 倒序显示，第 1 页最新。筛选用 `/日志搜索 <关键词>`"
            : $"> 倒序显示。换个词搜：`/日志搜索 <关键词>`");

        if (source.Remote)
        {
            builder.Append($"\n> 来源：{source.Label}");
        }

        // 翻页按钮：倒序下页码越大越早。
        List<(string Label, string Command)> buttons = [];
        if (current < totalPages)
        {
            buttons.Add(("⬅ 更早", PageCommand(keyword, current + 1)));
        }

        if (current > 1)
        {
            buttons.Add(("➡ 更新", PageCommand(keyword, current - 1)));
        }

        if (current != 1)
        {
            buttons.Add(("⏭ 最新", PageCommand(keyword, 1)));
        }

        buttons.Add(("下载最新", "/日志 下载"));
        buttons.Add(("打包全部", "/日志 打包"));
        buttons.Add(("文件列表", "/日志 文件"));

        await CommandHelpers.ReplyAsync(args, builder.ToString(), MenuKit.Keyboard([.. buttons]));
    }

    private static string PageCommand(string? keyword, int page)
    {
        return keyword is null ? $"/日志 {page}" : $"/日志搜索 {keyword} {page}";
    }

    private static async Task ListFilesAsync(CommandArgs args, LogSource source)
    {
        FileInfo[] files;
        try
        {
            files = LogFiles(source.Directory);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 读取目录失败\n> " + ex.Message);
            return;
        }

        if (files.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 📜 日志文件\n> 来源：{source.Label}\n> 目录里还没有日志：`{source.DisplayDirectory}`");
            return;
        }

        StringBuilder builder = new();
        builder.Append($"# 📜 日志文件（{files.Length}）\n");
        builder.Append("> 按时间倒序，序号可直接用来下载：`/日志 下载 <序号>`\n\n");

        for (int i = 0; i < files.Length && i < 15; i++)
        {
            FileInfo file = files[i];
            builder.Append($"{i + 1}. `{file.Name}`　{file.Length / 1024.0:F1} KB　{LogTime(file):MM-dd HH:mm}\n");
        }

        if (files.Length > 15)
        {
            builder.Append($"\n> 仅显示最近 15 个，共 {files.Length} 个。");
        }

        builder.Append($"\n\n> 来源：{source.Label}");
        builder.Append($"\n> 目录：`{source.DisplayDirectory}`");
        builder.Append($"\n\n> 只取一份：`/日志 下载 <序号>`　全部打包：`/日志 打包`");

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("下载最新", "/日志 下载"), ("打包全部", "/日志 打包"),
                ("刷新", "/日志 刷新"), ("返回日志", "/日志")));
    }

    // ── 下载 / 打包 / 区间 ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>/日志 下载 [序号 | 文件名]</c>：把一整份日志作为群文件发出来。
    /// 不带参数就是最新的那份 —— 排查完问题想把日志交给别人看，这是最直接的路径。
    /// </summary>
    private static async Task DownloadAsync(CommandArgs args, LogSource source, string selector)
    {
        FileInfo? file = ResolveLogFile(source, selector, out string error);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args, error);
            return;
        }

        if (file.Length > MaxSendBytes)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# ⛔ 日志文件过大\n> `{file.Name}` 有 {file.Length / 1048576.0:F1} MB，" +
                $"超过发送上限 {MaxSendBytes / 1048576} MB。\n> 用 `/日志 区间` 只取需要的部分。");
            return;
        }

        byte[] data;
        try
        {
            data = ReadAllBytes(file.FullName);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 读取日志失败\n> {ex.Message}\n> 文件：`{file.FullName}`");
            return;
        }

        if (data.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 日志文件是空的\n> `{file.Name}`");
            return;
        }

        await CommandHelpers.ReplyAsync(args,
            $"# 📜 日志文件\n- 文件：`{file.Name}`\n- 大小：{file.Length / 1024.0:F1} KB\n" +
            $"- 时间：{LogTime(file):yyyy-MM-dd HH:mm}" +
            (source.Remote ? $"\n- 来源：{source.Label}" : ""));

        await args.Adaptor.SendFile(data, file.Name, "text/plain");
    }

    /// <summary><c>/日志 打包</c>：把目录下所有 <c>*.log</c> 打成一个 zip 发出来。</summary>
    private static async Task PackAsync(CommandArgs args, LogSource source)
    {
        FileInfo[] files;
        try
        {
            files = LogFiles(source.Directory);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 读取目录失败\n> " + ex.Message);
            return;
        }

        if (files.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 📜 日志文件\n> 来源：{source.Label}\n> 目录里还没有日志：`{source.DisplayDirectory}`");
            return;
        }

        byte[] archive;
        long total = 0;
        try
        {
            using MemoryStream buffer = new();
            using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (FileInfo file in files)
                {
                    if (total + file.Length > MaxSendBytes)
                    {
                        await CommandHelpers.ReplyAsync(args,
                            $"# ⛔ 日志总量过大\n> 已超过打包上限 {MaxSendBytes / 1048576} MB，" +
                            "请先用「文件列表」挑出需要的，再用 `/日志 下载 <序号>` 单份取。");
                        return;
                    }

                    total += file.Length;

                    // 用共享读进压缩流：日志文件此刻可能仍被 TShock 的写入端持有。
                    ZipArchiveEntry entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                    using Stream entryStream = entry.Open();
                    using FileStream input = new(
                        file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    input.CopyTo(entryStream);
                }
            }

            archive = buffer.ToArray();
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 打包日志失败\n> {ex.Message}");
            return;
        }

        string name = $"logs-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        await CommandHelpers.ReplyAsync(args,
            $"# 📦 日志打包\n- 共 {files.Length} 份日志，原始 {total / 1048576.0:F1} MB\n" +
            $"- 压缩包：`{name}`（{archive.Length / 1024.0:F1} KB）");

        await args.Adaptor.SendFile(archive, name, "application/zip");
    }

    /// <summary>
    /// <c>/日志 区间 &lt;起&gt; &lt;止&gt; [序号 | 文件名]</c>：只看指定行号那一段。
    /// 行号按文件原始顺序（1 开始，含首尾），不做倒序 —— 既然是显式指定区间，就该按原样读。
    /// </summary>
    private static async Task SliceAsync(CommandArgs args, LogSource source)
    {
        if (args.Parameters.Length < 3
            || !args.TryGetInt(1, out int from)
            || !args.TryGetInt(2, out int to))
        {
            await CommandHelpers.ReplyAsync(args,
                "# 📜 查看日志区间\n> 用法：" +
                MenuKit.CmdInput("/日志 区间 ", "日志 区间 <起始行> <结束行> [序号]") + "\n" +
                "> 例：`/日志 区间 100 160`（第 100 到 160 行）\n" +
                "> 行号从 1 开始。先看总量：`/日志 文件`");
            return;
        }

        FileInfo? file = ResolveLogFile(source, args.GetOrDefault(3).Trim(), out string error);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args, error);
            return;
        }

        if (from > to)
        {
            (from, to) = (to, from);
        }

        from = Math.Max(1, from);
        List<string> lines;
        try
        {
            lines = ReadAllLines(file.FullName);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, $"# ⛔ 读取日志失败\n> {ex.Message}\n> 文件：`{file.FullName}`");
            return;
        }

        if (from > lines.Count)
        {
            await CommandHelpers.ReplyAsync(args,
                $"# 🔍 行号超出范围\n> `{file.Name}` 共 {lines.Count} 行，起始行写了 {from}。");
            return;
        }

        int end = Math.Min(to, lines.Count);
        int start = from - 1;
        int count = end - start;
        bool clipped = count > MaxSliceLines;

        if (clipped)
        {
            end = start + MaxSliceLines;
            count = MaxSliceLines;
        }

        StringBuilder content = new();
        for (int i = start; i < end; i++)
        {
            string raw = lines[i];
            content.Append(raw.Length > MaxLineLength ? raw[..MaxLineLength] + "…" : raw).Append('\n');
        }

        StringBuilder builder = new();
        builder.Append("# 📜 日志区间\n");
        builder.Append($"**{file.Name}**　第 {from}–{end} 行 / 共 {lines.Count} 行");
        if (clipped)
        {
            builder.Append($"（一次最多 {MaxSliceLines} 行，已截断）");
        }

        builder.Append("\n\n");
        builder.Append(MenuKit.CodeBlock(content.ToString()));
        builder.Append($"\n> 取整份：`/日志 下载`　全部打包：`/日志 打包`");

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("返回日志", "/日志"), ("文件列表", "/日志 文件"), ("打包全部", "/日志 打包")));
    }

    // ── 工具 ───────────────────────────────────────────────────────────────────

    /// <summary>读取日志全文。用共享读打开 —— TShock 正在往同一个文件里写，独占读会失败。</summary>
    private static List<string> ReadAllLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);

        List<string> lines = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static byte[] ReadAllBytes(string path)
    {
        // 同样必须共享读：日志文件此刻通常还被 TShock 的写入端持有。
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>目录下的日志文件，按时间倒序 —— 序号 1 就是最新的那份。</summary>
    private static FileInfo[] LogFiles(string directory)
    {
        return [.. new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(LogTime)];
    }

    /// <summary>
    /// 日志的「展示时间」。TShock 的日志名就是启动时间（<c>yyyy-MM-dd_HH-mm-ss.log</c>），
    /// 优先按文件名解析 —— 跨端镜像解压后 mtime 是打包时刻，只有文件名始终可靠。
    /// </summary>
    internal static DateTime LogTime(FileInfo file)
    {
        string stem = Path.GetFileNameWithoutExtension(file.Name);
        return DateTime.TryParseExact(
            stem,
            "yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsed)
            ? parsed
            : file.LastWriteTime;
    }

    private static string? LatestLogFile(string directory)
    {
        try
        {
            return LogFiles(directory).FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把「序号 / 文件名」解析成具体的一份日志。
    /// 纯数字按序号（与「文件列表」里的编号一致）；否则按文件名匹配，
    /// 精确匹配失败再退化成「包含」匹配 —— 日志名带时间戳，没人愿意完整敲一遍。
    /// </summary>
    private static FileInfo? ResolveLogFile(LogSource source, string selector, out string error)
    {
        error = "";

        FileInfo[] files;
        try
        {
            files = LogFiles(source.Directory);
        }
        catch (Exception ex)
        {
            error = $"# ⛔ 读取目录失败\n> {ex.Message}";
            return null;
        }

        if (files.Length == 0)
        {
            error = $"# 📜 日志文件\n> 来源：{source.Label}\n> 目录里还没有日志：`{source.DisplayDirectory}`";
            return null;
        }

        if (selector.Length == 0)
        {
            return files[0];
        }

        if (int.TryParse(selector, out int index))
        {
            if (index < 1 || index > files.Length)
            {
                error = $"# ⛔ 序号超出范围\n> 一共 {files.Length} 份日志，序号 1 是最新的。用 `/日志 文件` 查看清单。";
                return null;
            }

            return files[index - 1];
        }

        FileInfo? exact = files.FirstOrDefault(f => string.Equals(f.Name, selector, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        FileInfo[] partial = [.. files.Where(f => f.Name.Contains(selector, StringComparison.OrdinalIgnoreCase))];
        return partial.Length switch
        {
            1 => partial[0],
            0 => Fail(out error, $"# ⛔ 没有匹配的日志\n> 关键词：`{selector}`\n> 用 `/日志 文件` 查看全部清单。"),
            _ => Fail(out error, $"# ⛔ 匹配到 {partial.Length} 份日志\n> 请写得更具体，或用 `/日志 文件` 里的序号。"),
        };
    }

    private static FileInfo? Fail(out string error, string message)
    {
        error = message;
        return null;
    }

    /// <summary>
    /// 日志来源。<see cref="Directory"/> 是实际读取的本地路径（跨端时是镜像缓存目录），
    /// <see cref="DisplayDirectory"/> 是回显给用户看的位置描述。
    /// </summary>
    private sealed record LogSource(string Directory, string Label, bool Remote, string DisplayDirectory);

    /// <summary>
    /// 解析日志来源。默认优先「在线服务端」（跨机器可用），没有在线连接时回退本机目录。
    /// 返回 <c>null</c> 表示已经回过一条提示。
    /// </summary>
    private static async Task<LogSource?> ResolveSourceAsync(CommandArgs args, bool force)
    {
        string mode = (App.Config.LogSource ?? "").Trim().ToLowerInvariant();
        if (mode.Length == 0)
        {
            mode = "auto";
        }

        if (mode is not "local")
        {
            ServerSession? session = LiveSession(args);
            if (session is not null)
            {
                string? cacheDir = await RemoteLogMirror.EnsureAsync(args, session, force);
                if (cacheDir is not null)
                {
                    return new LogSource(
                        cacheDir,
                        $"服务端 {session.Record.ServerName}（跨端）",
                        Remote: true,
                        DisplayDirectory: $"服务端 {session.Record.ServerName} 的日志目录");
                }

                if (mode is "remote")
                {
                    return null;
                }

                // auto 模式下远端取不到就退回本机，至少让指令还有输出。
                Message.Yellow($"[{App.BotName}] 跨端取日志失败，已回退到本机目录。");
            }
            else if (mode is "remote")
            {
                await CommandHelpers.ReplyAsync(args,
                    "# ⛔ 没有在线服务端\n" +
                    "> `LogSource = remote` 时必须有服务端连接。\n" +
                    "> 看看在线情况：`/服务器列表`");
                return null;
            }
        }

        string configured = App.Config.TShockLogDirectory;
        string resolvedPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(WindyRuntime.BasicPath, configured));

        if (!Directory.Exists(resolvedPath))
        {
            await CommandHelpers.ReplyAsync(args, MissingDirectoryHint(resolvedPath));
            return null;
        }

        return new LogSource(resolvedPath, "本机目录", Remote: false, DisplayDirectory: resolvedPath);
    }

    /// <summary>当前群内在线且未关闭的服务端连接；没有则返回 <c>null</c>（不产生任何回复）。</summary>
    private static ServerSession? LiveSession(CommandArgs args)
    {
        if (!App.Ready)
        {
            return null;
        }

        string? groupOpenId = args.Message.GroupId;
        if (string.IsNullOrEmpty(groupOpenId))
        {
            return null;
        }

        ServerSession? session = App.Hub.FindByGroup(groupOpenId);
        return session is { IsOpen: true } ? session : null;
    }

    private static string MissingDirectoryHint(string path)
    {
        return "# ⛔ 找不到日志目录\n" +
               $"> 当前解析路径：`{path}`\n" +
               "> 请检查 `CaiBotWindy.json` 的 `TShockLogDirectory`（相对路径按机器人运行目录解析）。";
    }

    private static string UsageText()
    {
        return "# 📜 日志\n" +
               "> `/日志 [页码]`　分页查看最新日志（第 1 页最新）\n" +
               "> `/日志 区间 <起> <止> [序号]`　只看某段行号\n" +
               "> `/日志 文件`　列出全部日志及序号\n" +
               "> `/日志 下载 [序号 | 文件名]`　把一整份日志发到群里\n" +
               "> `/日志 打包`　全部历史日志打成一个 zip\n" +
               "> `/日志 刷新`　强制重新拉取服务端日志\n" +
               "> `/日志搜索 <关键词> [页码]`　按关键词检索\n\n" +
               "> 服务端在线时自动从服务端取（机器人可以不在同一台机器上）；\n" +
               "> 没有在线服务端时读本机 `TShockLogDirectory`。\n" +
               "> 取服务端任意路径的文件：`/发文件 <路径>`";
    }
}
