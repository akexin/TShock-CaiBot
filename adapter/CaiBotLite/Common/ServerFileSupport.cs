using System.Globalization;
using System.IO.Compression;
using System.Text;
using CaiBotLite.Models;
using Terraria;
using TShockAPI;

namespace CaiBotLite.Common;

/// <summary>
/// 「服务端文件」服务：应答机器人下发的 <c>ServerFile</c> 包，把服务端磁盘上的文件 / 目录 /
/// 日志回传给机器人，由机器人转发到 QQ 群。
///
/// <para><b>为什么单独开一个包类型</b>：<c>ServerLog</c> 已经被背包物品监控占用，
/// 那条链路的语义是「服务端主动上报一条事件」；而这里的语义刚好相反 ——
/// 机器人主动请求、服务端应答，属于请求-响应链路，混在一起会让两边的分发逻辑都变脏。</para>
///
/// <para><b>路径不设白名单</b>：能读到什么取决于 TShock 进程自身的权限。
/// 调用侧（机器人）已把这几条指令限定为群管理员，边界放在那一层更合适 ——
/// 在插件里再维护一份路径白名单，只会让「服务器换了目录结构后指令静默失效」。</para>
///
/// <para><b>动作（<c>action</c>）</b>：
/// <list type="bullet">
///   <item><c>info</c> —— 回报几个常用目录（服务端根 / 存档 / 日志 / 插件），用于「空参数」时的引导；</item>
///   <item><c>view</c> —— 查看：目录给清单，文本文件给内容预览，二进制文件只给元信息；</item>
///   <item><c>download</c> —— 取回文件本体；目录会被打包成 zip 一起回传。</item>
/// </list></para>
/// </summary>
internal static class ServerFileSupport
{
    /// <summary>
    /// 单次回传的原始字节上限。回传链路是「base64 → gzip → base64」，
    /// 体积会膨胀到约 1.8 倍，而机器人侧单包接收上限是 64 MB —— 32 MB 留足余量。
    /// </summary>
    private const long MaxTransferBytes = 32L * 1024 * 1024;

    /// <summary>文本预览一次最多返回的字符数（超出部分截断，只用于「看一眼」，取全文请用 download）。</summary>
    private const int MaxPreviewChars = 20_000;

    /// <summary>二进制判定只看开头这么多字节，避免为了判断把整个文件读进来。</summary>
    private const int BinaryProbeBytes = 8_000;

    /// <summary>目录清单一页最多列出的条目数。</summary>
    private const int MaxListEntries = 300;

    internal static void Handle(Package package, PackageWriter writer)
    {
        var action = (Text(package, "action") ?? "info").Trim().ToLowerInvariant();

        try
        {
            switch (action)
            {
                case "info":
                    WriteInfo(writer);
                    break;

                case "view":
                    WriteView(writer, ResolvePath(Text(package, "path"), required: true));
                    break;

                case "download":
                    WriteDownload(writer, ResolvePath(Text(package, "path"), required: true));
                    break;

                default:
                    Fail(writer, $"不支持的动作：{action}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // 这里把异常转成正常的应答包，而不是抛出去 —— 抛出去会让整条连接的回包
            // 变成 Error 包，机器人那边只能拿到底层异常文本，看不出是「路径不存在」这类业务错误。
            Fail(writer, ex.Message);
        }
    }

    // ── info ───────────────────────────────────────────────────────────────────

    private static void WriteInfo(PackageWriter writer)
    {
        var worldPath = Main.worldPathName;
        var savePath = ResolvePath(TShock.SavePath, required: false);

        writer
            .Write("ok", true)
            .Write("action", "info")
            .Write("base_dir", Environment.CurrentDirectory)
            .Write("save_dir", savePath)
            .Write("log_dir", Path.Combine(savePath, "logs"))
            .Write("world_dir", string.IsNullOrEmpty(worldPath) ? "" : Path.GetDirectoryName(Path.GetFullPath(worldPath))!)
            .Write("world_name", string.IsNullOrEmpty(worldPath) ? "" : Path.GetFileName(worldPath))
            .Write("plugin_dir", Path.GetFullPath("ServerPlugins"))
            .Send();
    }

    // ── view ───────────────────────────────────────────────────────────────────

    private static void WriteView(PackageWriter writer, string path)
    {
        if (Directory.Exists(path))
        {
            WriteDirectoryListing(writer, new DirectoryInfo(path));
            return;
        }

        if (!File.Exists(path))
        {
            Fail(writer, $"路径不存在：{path}");
            return;
        }

        var info = new FileInfo(path);
        writer
            .Write("ok", true)
            .Write("action", "view")
            .Write("path", info.FullName)
            .Write("name", info.Name)
            .Write("size", info.Length)
            .Write("mtime", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        // 超大文件不预览：读进来只会把这一轮指令拖成几秒，用户看到的还是截断结果。
        if (info.Length > 8L * 1024 * 1024)
        {
            writer
                .Write("kind", "binary")
                .Write("message", $"文件较大（{Human(info.Length)}），已跳过文本预览，请用「发文件」取回。")
                .Send();
            return;
        }

        var bytes = ReadAllBytesShared(path);
        if (IsBinary(bytes))
        {
            writer
                .Write("kind", "binary")
                .Write("message", "这是二进制文件，无法文本预览，请用「发文件」取回。")
                .Send();
            return;
        }

        // TShock 的日志与配置一律 UTF-8；用 UTF-8 解码即可，
        // 真正的二进制在上一步已经拦掉了。
        var text = Encoding.UTF8.GetString(bytes);
        var truncated = false;
        if (text.Length > MaxPreviewChars)
        {
            text = text[..MaxPreviewChars];
            truncated = true;
        }

        writer
            .Write("kind", "text")
            .Write("lines", CountLines(bytes))
            .Write("text", text)
            .Write("truncated", truncated)
            .Send();
    }

    private static void WriteDirectoryListing(PackageWriter writer, DirectoryInfo directory)
    {
        DirectoryInfo[] directories;
        FileInfo[] files;
        try
        {
            directories = directory.GetDirectories();
            files = directory.GetFiles();
        }
        catch (Exception ex)
        {
            Fail(writer, $"读取目录失败：{ex.Message}");
            return;
        }

        Array.Sort(directories, (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Array.Sort(files, (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var total = directories.Length + files.Length;
        var builder = new StringBuilder();
        var shown = 0;

        // 先目录后文件，各自按名字排：和资源管理器的观感一致，也方便按名字找。
        foreach (var child in directories)
        {
            if (shown >= MaxListEntries)
            {
                break;
            }

            builder.Append($"[目录] {child.Name}/\n");
            shown++;
        }

        foreach (var file in files)
        {
            if (shown >= MaxListEntries)
            {
                break;
            }

            builder.Append($"[文件] {file.Name}  {Human(file.Length)}  {file.LastWriteTime:yyyy-MM-dd HH:mm}\n");
            shown++;
        }

        writer
            .Write("ok", true)
            .Write("action", "view")
            .Write("kind", "dir")
            .Write("path", directory.FullName)
            .Write("name", directory.Name)
            .Write("count", total)
            .Write("dirs", directories.Length)
            .Write("files", files.Length)
            .Write("truncated", total > MaxListEntries)
            .Write("text", builder.ToString().TrimEnd('\n'))
            .Send();
    }

    // ── download ───────────────────────────────────────────────────────────────

    private static void WriteDownload(PackageWriter writer, string path)
    {
        if (Directory.Exists(path))
        {
            var (data, _) = ZipDirectory(new DirectoryInfo(path));
            SendBinary(writer, data, $"{new DirectoryInfo(path).Name}.zip", isFolder: true);
            return;
        }

        if (!File.Exists(path))
        {
            Fail(writer, $"路径不存在：{path}");
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > MaxTransferBytes)
        {
            Fail(writer, $"文件过大（{Human(info.Length)}），单次回传上限 {Human(MaxTransferBytes)}。");
            return;
        }

        SendBinary(writer, ReadAllBytesShared(path), info.Name, isFolder: false);
    }

    private static void SendBinary(PackageWriter writer, byte[] data, string name, bool isFolder)
    {
        if (data.LongLength > MaxTransferBytes)
        {
            Fail(writer, $"内容过大（{Human(data.LongLength)}），单次回传上限 {Human(MaxTransferBytes)}。");
            return;
        }

        writer
            .Write("ok", true)
            .Write("action", "download")
            .Write("name", name)
            .Write("folder", isFolder)
            .Write("size", data.LongLength)

            // 与 world_file / map_file 完全相同的两层编码，机器人侧用同一个解码器还原。
            .Write("base64", Utils.CompressBase64(Convert.ToBase64String(data)))
            .Send();
    }

    /// <summary>把目录整体打包成 zip。条目名用相对路径，解压后是原本的目录结构。</summary>
    private static (byte[] Data, int Entries) ZipDirectory(DirectoryInfo directory)
    {
        var rootLength = directory.FullName.Length + 1;
        var entries = 0;

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (file.Length > MaxTransferBytes)
                {
                    throw new InvalidOperationException($"目录内存在超大文件：{file.FullName}（{Human(file.Length)}）");
                }

                var entryName = file.FullName[rootLength..].Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var input = OpenShared(file.FullName);
                input.CopyTo(entryStream);
                entries++;

                if (output.Length > MaxTransferBytes)
                {
                    throw new InvalidOperationException($"目录内容过大，超过单次回传上限 {Human(MaxTransferBytes)}。");
                }
            }
        }

        return (output.ToArray(), entries);
    }

    // ── 工具 ───────────────────────────────────────────────────────────────────

    /// <summary>把请求里的路径解析成绝对路径；<paramref name="required"/> 为真时不允许留空。</summary>
    private static string ResolvePath(string? path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
            {
                throw new InvalidOperationException("缺少 path 参数。");
            }

            return Environment.CurrentDirectory;
        }

        // 去掉用户顺手带上的引号：Windows 路径带空格时很常见。
        var trimmed = path.Trim().Trim('"');
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(Environment.CurrentDirectory, expanded));
    }

    /// <summary>
    /// 共享读打开文件。必须带 <see cref="FileShare.ReadWrite"/> ——
    /// 日志文件此刻正被 TShock 的写入端持有，独占读会直接抛共享冲突。
    /// </summary>
    private static FileStream OpenShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = OpenShared(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>开头出现 NUL 字节就当成二进制 —— 文本文件（含 UTF-8 BOM）不会有。</summary>
    private static bool IsBinary(byte[] data)
    {
        var limit = Math.Min(data.Length, BinaryProbeBytes);
        for (var i = 0; i < limit; i++)
        {
            if (data[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountLines(byte[] data)
    {
        var lines = 1;
        foreach (var b in data)
        {
            if (b == (byte)'\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string Human(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    private static string? Text(Package package, string key)
    {
        return package.Payload.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
    }

    private static void Fail(PackageWriter writer, string message)
    {
        writer
            .Write("ok", false)
            .Write("error", message)
            .Send();
    }
}
