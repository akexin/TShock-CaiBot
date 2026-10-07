using System.IO.Compression;
using System.Text;
using CaiBotWindy.Services;
using Windy.SDK;
using Windy.SDK.Adaptor;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Commands;

/// <summary>
/// TShock 服务端日志的查看与检索。
///
/// <para>日志目录由配置项 <c>TShockLogDirectory</c> 指定（默认指向同仓库下的 TShock 服务端）。
/// TShock 每次启动会新开一个 <c>日期_时间.log</c>，所以这里默认读**最新的那个**。</para>
///
/// <para>分页是倒序的：**第 1 页是最新的若干行** —— 排查问题时几乎总是想看最新的。</para>
///
/// <para>四种查看方式，覆盖「快速扫一眼」到「把整份日志拿走」：
/// <list type="bullet">
///   <item><c>/日志</c> —— 分页扫最新日志；</item>
///   <item><c>/日志 区间 起 止</c> —— 只看某一段行号；</item>
///   <item><c>/日志 下载 [序号]</c> —— 把某一份完整日志作为群文件发出来；</item>
///   <item><c>/日志 打包</c> —— 全部历史日志打成一个 zip 发出来。</item>
/// </list>
/// 后两者走的是「读本地目录 → 发群文件」，与 <c>/日志</c> 读的是同一个目录
/// （机器人需要与 TShock 同机，或该目录可访问）。要取服务端任意路径的文件，用 <c>/发文件</c>。</para>
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

        string? directory = ResolveLogDirectory(out string path);
        if (directory is null)
        {
            await CommandHelpers.ReplyAsync(args, MissingDirectoryHint(path));
            return;
        }

        string sub = args.GetOrDefault(0).Trim();

        switch (sub)
        {
            case "文件" or "files" or "list":
                await ListFilesAsync(args, directory);
                return;

            case "下载" or "发送" or "download":
                await DownloadAsync(args, directory, args.GetOrDefault(1).Trim());
                return;

            case "打包" or "zip" or "全部":
                await PackAsync(args, directory);
                return;

            case "区间" or "部分" or "range":
                await SliceAsync(args, directory);
                return;

            case "帮助" or "help" or "?":
                await CommandHelpers.ReplyAsync(args, UsageText());
                return;
        }

        string? file = LatestLogFile(directory);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args, $"# 📜 服务器日志\n> 目录里还没有日志文件：`{directory}`");
            return;
        }

        int page = int.TryParse(sub, out int parsed) && parsed > 0 ? parsed : 1;
        await RenderAsync(args, file, page, keyword: null);
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

        string? directory = ResolveLogDirectory(out string path);
        if (directory is null)
        {
            await CommandHelpers.ReplyAsync(args, MissingDirectoryHint(path));
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

        string keyword = args.GetOrDefault(0).Trim();
        int page = args.TryGetInt(1, out int parsed) && parsed > 0 ? parsed : 1;

        string? file = LatestLogFile(directory);
        if (file is null)
        {
            await CommandHelpers.ReplyAsync(args, $"# 📜 服务器日志\n> 目录里还没有日志文件：`{directory}`");
            return;
        }

        await RenderAsync(args, file, page, keyword);
    }

    // ── 渲染 ───────────────────────────────────────────────────────────────────

    private static async Task RenderAsync(CommandArgs args, string file, int page, string? keyword)
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

    private static async Task ListFilesAsync(CommandArgs args, string directory)
    {
        FileInfo[] files;
        try
        {
            files = LogFiles(directory);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 读取目录失败\n> " + ex.Message);
            return;
        }

        if (files.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"# 📜 日志文件\n> 目录里还没有日志：`{directory}`");
            return;
        }

        StringBuilder builder = new();
        builder.Append($"# 📜 日志文件（{files.Length}）\n");
        builder.Append("> 按修改时间倒序，序号可直接用来下载：`/日志 下载 <序号>`\n\n");

        for (int i = 0; i < files.Length && i < 15; i++)
        {
            FileInfo file = files[i];
            builder.Append($"{i + 1}. `{file.Name}`　{file.Length / 1024.0:F1} KB　{file.LastWriteTime:MM-dd HH:mm}\n");
        }

        if (files.Length > 15)
        {
            builder.Append($"\n> 仅显示最近 15 个，共 {files.Length} 个。");
        }

        builder.Append($"\n\n> 目录：`{directory}`");
        builder.Append($"\n\n> 只取一份：`/日志 下载 <序号>`　全部打包：`/日志 打包`");

        await CommandHelpers.ReplyAsync(args, builder.ToString(),
            MenuKit.Keyboard(("下载最新", "/日志 下载"), ("打包全部", "/日志 打包"), ("返回日志", "/日志")));
    }

    // ── 下载 / 打包 / 区间 ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>/日志 下载 [序号 | 文件名]</c>：把一整份日志作为群文件发出来。
    /// 不带参数就是最新的那份 —— 排查完问题想把日志交给别人看，这是最直接的路径。
    /// </summary>
    private static async Task DownloadAsync(CommandArgs args, string directory, string selector)
    {
        FileInfo? file = ResolveLogFile(directory, selector, out string error);
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
            $"- 修改：{file.LastWriteTime:yyyy-MM-dd HH:mm}");

        await args.Adaptor.SendFile(data, file.Name, "text/plain");
    }

    /// <summary><c>/日志 打包</c>：把目录下所有 <c>*.log</c> 打成一个 zip 发出来。</summary>
    private static async Task PackAsync(CommandArgs args, string directory)
    {
        FileInfo[] files;
        try
        {
            files = LogFiles(directory);
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args, "# ⛔ 读取目录失败\n> " + ex.Message);
            return;
        }

        if (files.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args, $"# 📜 日志文件\n> 目录里还没有日志：`{directory}`");
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
    private static async Task SliceAsync(CommandArgs args, string directory)
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

        FileInfo? file = ResolveLogFile(directory, args.GetOrDefault(3).Trim(), out string error);
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

    /// <summary>目录下的日志文件，按修改时间倒序 —— 序号 1 就是最新的那份。</summary>
    private static FileInfo[] LogFiles(string directory)
    {
        return [.. new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(file => file.LastWriteTime)];
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
    private static FileInfo? ResolveLogFile(string directory, string selector, out string error)
    {
        error = "";

        FileInfo[] files;
        try
        {
            files = LogFiles(directory);
        }
        catch (Exception ex)
        {
            error = $"# ⛔ 读取目录失败\n> {ex.Message}";
            return null;
        }

        if (files.Length == 0)
        {
            error = $"# 📜 日志文件\n> 目录里还没有日志：`{directory}`";
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

    /// <summary>解析日志目录；目录不存在时返回 null，并回传解析后的路径便于提示。</summary>
    private static string? ResolveLogDirectory(out string resolvedPath)
    {
        string configured = App.Config.TShockLogDirectory;
        resolvedPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(WindyRuntime.BasicPath, configured));

        return Directory.Exists(resolvedPath) ? resolvedPath : null;
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
               "> `/日志搜索 <关键词> [页码]`　按关键词检索\n\n" +
               "> 取服务端任意路径的文件：`/发文件 <路径>`";
    }
}
