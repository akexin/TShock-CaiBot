namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 配置文件 / 数据库的自动备份。
///
/// <para><b>为什么需要它</b>：<c>store.json</c> 装着全部群绑定、用户白名单、云黑名单、
/// 管理员列表 —— 这是机器人<b>唯一的持久化资产</b>，丢了只能让大家重新绑一遍。
/// 而它的写入非常频繁（每次进服校验、每次签到都会落盘），一旦某次写入逻辑有 bug
/// 写出坏数据，等发现时早已覆盖过多轮。</para>
///
/// <para><b>策略</b>：每次正式写入<b>之前</b>先把当前内容复制一份到同级 <c>.backup/</c> 目录，
/// 文件名带时间戳。只保留最近 N 份，其余自动清理 —— 备份本身也要防止无限增长。</para>
///
/// <para><b>目录布局</b>：<c>Config/CaiBotWindy/store.json</c> 的备份落在
/// <c>Config/CaiBotWindy/.backup/store-20261007-013000.json</c>。
/// 放在原目录同级（而不是全局备份目录），是为了保证同卷、复制快，
/// 也方便出问题时就地找回。</para>
/// </summary>
public static class BackupService
{
    /// <summary>备份目录名（隐藏目录，避免污染主目录）。</summary>
    private const string BackupDirectoryName = ".backup";

    /// <summary>默认保留份数。覆盖约一周的密集写入，足够回滚，又不至于撑爆磁盘。</summary>
    public const int DefaultKeepCount = 7;

    /// <summary>
    /// 为 <paramref name="path"/> 创建一份备份。
    /// </summary>
    /// <param name="path">要备份的文件（必须已存在）。</param>
    /// <param name="keepCount">保留份数，超出后按时间从旧到新清理。小于等于 0 表示不清理。</param>
    /// <returns>新备份的完整路径；文件不存在时返回 null。</returns>
    public static string? Create(string path, int keepCount = DefaultKeepCount)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string backupDirectory = Path.Combine(directory, BackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);

        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        // 时间戳精确到秒。同一秒内重复备份会撞名，加一个序号兜底。
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string target = Path.Combine(backupDirectory, $"{stem}-{stamp}{extension}");
        if (File.Exists(target))
        {
            // 注意写法：必须先 ToString("N") 再切片。写成 {Guid.NewGuid():N[..4]}
            // 会被当成格式字符串 "N[..4]"，运行时直接抛 FormatException。
            target = Path.Combine(backupDirectory, $"{stem}-{stamp}-{Guid.NewGuid().ToString("N")[..4]}{extension}");
        }

        File.Copy(path, target, overwrite: true);

        if (keepCount > 0)
        {
            Prune(backupDirectory, stem, extension, keepCount);
        }

        return target;
    }

    /// <summary>
    /// 列出某个文件的全部备份，<b>最新在前</b>（回退时应该优先试最新的）。
    /// </summary>
    public static List<string> List(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string backupDirectory = Path.Combine(directory, BackupDirectoryName);
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string prefix = $"{stem}-";

        return Directory.GetFiles(backupDirectory, $"{stem}-*{extension}")
            .Where(file => Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => new FileInfo(file).LastWriteTimeUtc)
            .ThenByDescending(file => file, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>删除某个文件的全部备份。仅在明确需要彻底重置时使用。</summary>
    public static int Clear(string path)
    {
        List<string> backups = List(path);
        int removed = 0;

        foreach (string file in backups)
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // 被占用时忽略，剩下的下次再清。
            }
        }

        return removed;
    }

    /// <summary>
    /// 保留最近 <paramref name="keepCount"/> 份，删除更旧的。
    ///
    /// <para>按「文件名时间戳」而不是「修改时间」排序：复制出来的备份修改时间等于复制时刻，
    /// 两者通常一致，但手工改动过备份文件时文件名更可靠。</para>
    /// </summary>
    private static void Prune(string backupDirectory, string stem, string extension, int keepCount)
    {
        string prefix = $"{stem}-";

        List<string> all;
        try
        {
            all = Directory.GetFiles(backupDirectory, $"{stem}-*{extension}")
                .Where(file => Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return;
        }

        foreach (string stale in all.Skip(keepCount))
        {
            try
            {
                File.Delete(stale);
            }
            catch
            {
                // 删不掉就算了，下次写入会再试。
            }
        }
    }
}
