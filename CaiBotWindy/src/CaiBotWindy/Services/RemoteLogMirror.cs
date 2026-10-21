using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CaiBotWindy.Commands;
using CaiBotWindy.Infrastructure;
using CaiBotWindy.Net;
using CaiBotWindy.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windy.SDK;
using Windy.SDK.Command;
using Windy.SDK.Events;

namespace CaiBotWindy.Services;

/// <summary>
/// 把「运行 TShock 那台机器」上的日志目录镜像到机器人本地缓存。
///
/// <para><b>为什么是镜像而不是逐条远程读取</b>：<c>/日志</c> 的五种用法（分页 / 区间 / 搜索 /
/// 下载 / 打包）都需要「把整份文本读进来」再切片。若每次翻页都走一次远程读，网络往返会把
/// 翻页拖成几秒；而镜像一个目录只需要一次 <c>server_file</c> 的 <c>download</c>（适配插件侧的
/// 目录会被自动打包成 zip），之后所有切片都在本地完成，行为与「机器人与 TShock 同机」完全一致。</para>
///
/// <para><b>刷新策略</b>：<c>/日志 刷新</c> 强制重拉；否则先探一次远端目录条目数，数量变了
/// （TShock 重启会新开一份 <c>日期_时间.log</c>）立刻重拉，数量没变则按
/// <see cref="PluginConfig.RemoteLogCacheSeconds"/> 的间隔刷新内容。</para>
///
/// <para><b>时间戳</b>：适配插件打包时不写入条目原始 mtime，解压后所有文件的修改时间都会
/// 退化成打包时刻。因此展示与排序都以「文件名里的时间戳」为准，见
/// <c>LogCommands.LogTime</c>。</para>
/// </summary>
internal static class RemoteLogMirror
{
    /// <summary>
    /// <c>server_file</c> 包的引入版本。低于它的适配插件不会回包，只能干等超时 ——
    /// 先按版本号挡掉，把 60 秒的等待换成一句明确的提示。
    /// </summary>
    private const string RequiredPluginVersion = "2026.10.3.2";

    /// <summary>镜像缓存根目录（相对机器人运行目录）。</summary>
    private const string CacheRoot = "cache/remote-logs";

    /// <summary>缓存目录里的元信息文件名。</summary>
    private const string MarkerName = "mirror.json";

    /// <summary>
    /// 按缓存目录加锁。两个人同时发 <c>/日志</c> 时，不能让两个解压流程同时写同一批文件。
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 确保本地镜像可用，返回镜像目录的绝对路径；失败时返回 <c>null</c>（并已回过一条提示）。
    /// </summary>
    /// <param name="force">为真时忽略缓存直接重拉（<c>/日志 刷新</c>）。</param>
    public static async Task<string?> EnsureAsync(CommandArgs args, ServerSession session, bool force)
    {
        if (!EnsureSupported(session, out string version))
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 适配插件版本过低\n" +
                $"> 当前服务端适配插件为 `{version}`，跨端读日志需要 **{RequiredPluginVersion}** 及以上。\n" +
                "> 更新服务端的 CaiBotLite 适配插件后重试。");
            return null;
        }

        string? remoteDir = await FetchLogDirectoryAsync(args, session);
        if (remoteDir is null)
        {
            return null;
        }

        string cacheDir = CacheDirectory(session, remoteDir);
        SemaphoreSlim gate = Gates.GetOrAdd(cacheDir, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await EnsureCoreAsync(args, session, remoteDir, cacheDir, force);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string?> EnsureCoreAsync(
        CommandArgs args, ServerSession session, string remoteDir, string cacheDir, bool force)
    {
        MirrorMarker? marker = ReadMarker(cacheDir);
        bool hasCache = marker is not null && Directory.Exists(cacheDir);

        // 只有已经有缓存时才值得探测远端：没缓存无论如何都要拉一份。
        int? remoteFiles = hasCache ? await ProbeFileCountAsync(args, session, remoteDir) : null;

        if (!force && hasCache && !IsStale(marker!, remoteDir, remoteFiles))
        {
            return cacheDir;
        }

        byte[]? archive = await DownloadAsync(args, session, remoteDir);
        if (archive is null)
        {
            if (!hasCache)
            {
                return null;
            }

            // 刷新失败但有旧快照：宁可给一份略带延迟的日志，也别让指令直接不可用。
            Message.Yellow(
                $"[{App.BotName}] 日志镜像刷新失败，继续使用 {marker!.SyncedAtUtc.ToLocalTime():MM-dd HH:mm} 的缓存。");
            return cacheDir;
        }

        int files;
        try
        {
            files = Extract(archive, cacheDir);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[{App.BotName}] 日志镜像解压失败: {ex.Message}");
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 日志镜像解压失败\n" +
                $"> {ex.Message}\n" +
                $"> 目录：`{remoteDir}`");

            return hasCache ? cacheDir : null;
        }

        WriteMarker(cacheDir, new MirrorMarker
        {
            RemoteDir = remoteDir,
            SyncedAtUtc = DateTime.UtcNow,

            // 基线一律取「远端探测值」：只有两边口径一致，下次比较才有意义。
            // 探测失败时沿用旧基线，0 表示未知（跳过比较）。
            FileCount = remoteFiles ?? marker?.FileCount ?? 0,
            ExtractedFiles = files,
            ArchiveBytes = archive.LongLength,
            ServerName = session.Record.ServerName,
        });

        return cacheDir;
    }

    /// <summary>镜像目录的绝对路径。按键值隔离，避免多台服务端互相覆盖。</summary>
    public static string CacheDirectory(ServerSession session, string remoteDir)
    {
        string key = $"{Sanitize(session.Record.ServerName)}-{ShortHash(remoteDir)}";
        return Path.Combine(WindyRuntime.BasicPath, CacheRoot, key);
    }

    /// <summary>缓存的最近同步时间；没有缓存时返回 <c>null</c>。</summary>
    public static DateTime? SyncedAt(ServerSession session, string remoteDir)
    {
        return ReadMarker(CacheDirectory(session, remoteDir))?.SyncedAtUtc;
    }

    // ── 远端交互 ───────────────────────────────────────────────────────────────

    private static bool EnsureSupported(ServerSession session, out string version)
    {
        version = session.Record.PluginVersion;
        if (!Version.TryParse(version, out Version? parsed))
        {
            // 版本号缺失时放行：老适配插件普遍不报版本，硬挡会把能用的情况也挡掉。
            return true;
        }

        return parsed >= Version.Parse(RequiredPluginVersion);
    }

    /// <summary>问适配插件「日志目录在哪」。不自己拼路径 —— 各家的 <c>SavePath</c> 不一样。</summary>
    private static async Task<string?> FetchLogDirectoryAsync(CommandArgs args, ServerSession session)
    {
        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.ServerFile, new JObject { ["action"] = "info" });

        if (packet is null)
        {
            return null;
        }

        if (!packet.Payload.GetBool("ok"))
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 取服务端目录信息失败\n" +
                $"> {packet.Payload.GetString("error", "服务端没有说明原因")}");
            return null;
        }

        string logDir = packet.Payload.GetString("log_dir");
        if (logDir.Length == 0)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 服务端没有报告日志目录\n" +
                "> 适配插件版本可能过旧。也可以直接指定：`/看文件 tshock/logs`");
            return null;
        }

        return logDir;
    }

    /// <summary>
    /// 探测远端日志目录的条目数，用于判断「是否出现过新日志」。
    /// 探测本身失败不算错误 —— 返回 <c>null</c>，调用方按「没变化」处理。
    /// </summary>
    private static async Task<int?> ProbeFileCountAsync(CommandArgs args, ServerSession session, string remoteDir)
    {
        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.ServerFile,
            new JObject { ["action"] = "view", ["path"] = remoteDir });

        if (packet is null || !packet.Payload.GetBool("ok"))
        {
            return null;
        }

        // 用 files 而不是 count：count 把子目录也算进去了，两边口径不一致会一直判成「有变化」。
        return packet.Payload["files"]?.Value<int>();
    }

    /// <summary>取整份日志目录（适配插件侧会自动打包成 zip）。</summary>
    private static async Task<byte[]?> DownloadAsync(CommandArgs args, ServerSession session, string remoteDir)
    {
        BotPacket? packet = await CommandHelpers.RequestAsync(
            args, session, PackageType.ServerFile,
            new JObject { ["action"] = "download", ["path"] = remoteDir },
            fileRequest: true);

        if (packet is null)
        {
            return null;
        }

        if (!packet.Payload.GetBool("ok"))
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 取服务端日志失败\n" +
                $"> 目录：`{remoteDir}`\n" +
                $"> {packet.Payload.GetString("error", "服务端没有说明原因")}\n" +
                "> 确认目录：`/看文件`（不带参数会回报服务端上报的日志目录）\n" +
                "> 也可以 `/发文件 <路径>` 直接按路径取");
            return null;
        }

        try
        {
            return BinaryCodec.Decode(packet.Payload.GetString("base64"));
        }
        catch (Exception ex)
        {
            await CommandHelpers.ReplyAsync(args,
                "# ⛔ 日志包解码失败\n" +
                $"> {ex.Message}");
            return null;
        }
    }

    // ── 缓存 ───────────────────────────────────────────────────────────────────

    /// <summary>缓存是否已经不值得复用。</summary>
    private static bool IsStale(MirrorMarker marker, string remoteDir, int? remoteFiles)
    {
        if (!string.Equals(marker.RemoteDir, remoteDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 远端多了一份日志（TShock 重启）就该立刻重拉，不必等间隔。
        // FileCount 为 0 表示上次探测失败，基线不可比，跳过这一项。
        if (remoteFiles is int actual && marker.FileCount > 0 && actual != marker.FileCount)
        {
            return true;
        }

        int ttl = App.Config.RemoteLogCacheSeconds;
        return ttl <= 0 || (DateTime.UtcNow - marker.SyncedAtUtc).TotalSeconds >= ttl;
    }

    /// <summary>
    /// 把 zip 解到缓存目录。返回解出的文件数。
    /// 条目名做穿越防护（zip 可能被伪造），并清掉远端已经不存在的旧日志。
    /// </summary>
    internal static int Extract(byte[] archive, string cacheDir)
    {
        Directory.CreateDirectory(cacheDir);

        HashSet<string> extracted = new(StringComparer.OrdinalIgnoreCase);
        int files = 0;

        using (MemoryStream buffer = new(archive))
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                string relative = entry.FullName.Replace('\\', '/');
                if (relative.Contains("..", StringComparison.Ordinal) || relative.StartsWith('/'))
                {
                    continue;
                }

                string target = Path.GetFullPath(Path.Combine(cacheDir, relative));
                if (!target.StartsWith(Path.GetFullPath(cacheDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);

                extracted.Add(relative);
                files++;
            }
        }

        // 远端已经轮转掉的旧日志不该继续留在缓存里，否则「文件列表」会越用越长。
        foreach (string stale in Directory.EnumerateFiles(cacheDir, "*.log"))
        {
            if (!extracted.Contains(Path.GetFileName(stale)))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception ex)
                {
                    Message.Yellow($"[{App.BotName}] 清理过期日志缓存失败（可忽略）: {ex.Message}");
                }
            }
        }

        return files;
    }

    private static MirrorMarker? ReadMarker(string cacheDir)
    {
        string path = Path.Combine(cacheDir, MarkerName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<MirrorMarker>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            // 元信息坏了就当没有缓存，重拉一份即可。
            return null;
        }
    }

    private static void WriteMarker(string cacheDir, MirrorMarker marker)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            AtomicFile.Write(Path.Combine(cacheDir, MarkerName),
                JsonConvert.SerializeObject(marker, Formatting.Indented), backup: false);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[{App.BotName}] 写日志镜像元信息失败（可忽略）: {ex.Message}");
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "server";
        }

        StringBuilder builder = new();
        foreach (char ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }

        string sanitized = builder.ToString().Trim('_');
        return sanitized.Length == 0 ? "server" : sanitized[..Math.Min(24, sanitized.Length)];
    }

    private static string ShortHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <summary>缓存元信息。字段名不用特性映射，Newtonsoft 默认按属性名匹配即可。</summary>
    private sealed class MirrorMarker
    {
        public string RemoteDir { get; set; } = "";

        public DateTime SyncedAtUtc { get; set; }

        public int FileCount { get; set; }

        /// <summary>本次解出的文件数，仅用于排查。</summary>
        public int ExtractedFiles { get; set; }

        public long ArchiveBytes { get; set; }

        public string ServerName { get; set; } = "";
    }
}
