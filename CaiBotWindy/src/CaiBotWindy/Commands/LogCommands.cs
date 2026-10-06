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
/// </summary>
public static class LogCommands
{
    /// <summary>每页行数。QQ 单条 markdown 消息有长度上限，20 行是稳妥值。</summary>
    private const int PageSize = 20;

    /// <summary>单行最大展示长度。异常堆栈那种超长行会把整页挤爆，超了截断。</summary>
    private const int MaxLineLength = 120;

    // ── /日志 [页码 | 文件] ────────────────────────────────────────────────────

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

        if (sub is "文件" or "files" or "list")
        {
            await ListFilesAsync(args, directory);
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
            files = [.. new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime)];
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
        builder.Append("> 按修改时间倒序，`/日志` 默认读第一个。\n\n");

        foreach (FileInfo file in files.Take(15))
        {
            builder.Append($"- `{file.Name}`　{file.Length / 1024.0:F1} KB　{file.LastWriteTime:MM-dd HH:mm}\n");
        }

        if (files.Length > 15)
        {
            builder.Append($"\n> 仅显示最近 15 个，共 {files.Length} 个。");
        }

        builder.Append($"\n\n> 目录：`{directory}`");

        await CommandHelpers.ReplyAsync(args, builder.ToString());
    }

    // ── 工具 ───────────────────────────────────────────────────────────────────

    /// <summary>读取日志全文。用共享读打开 —— TShock 正在往同一个文件里写，独占读会失败。</summary>
    private static List<string> ReadAllLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream, Encoding.UTF8);

        List<string> lines = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static string? LatestLogFile(string directory)
    {
        try
        {
            return new DirectoryInfo(directory)
                .GetFiles("*.log")
                .OrderByDescending(file => file.LastWriteTime)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
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
}
