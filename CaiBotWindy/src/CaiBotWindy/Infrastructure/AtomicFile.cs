using System.Text;
using Windy.SDK;

namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 安全的配置文件写入 —— 原子替换 + 可选备份。
///
/// <para><b>为什么需要它</b>：直接 <c>File.WriteAllText</c> 覆盖目标文件时，如果写到一半进程崩溃、
/// 磁盘满、或被杀毒软件打断，文件会停在「半个 JSON」的状态。配置文件一旦损坏，
/// 机器人<b>下一次就起不来了</b>（反序列化失败），而且此时已经没有机会再自我修复。</para>
///
/// <para><b>做法</b>：先写同目录下的临时文件，写完后用一次 <c>File.Move(overwrite)</c> 顶替正式文件。
/// NTFS 上这个替换是原子的 —— 要么看到完整的旧文件，要么看到完整的新文件，
/// 不存在「中间态」。再叠加一层备份，即使新内容本身是错的也能回滚。</para>
///
/// <para><b>顺序很重要</b>：备份必须在替换<b>之前</b>完成。否则一旦替换成功而备份失败，
/// 旧内容就已经没了。</para>
/// </summary>
public static class AtomicFile
{
    /// <summary>临时文件后缀。放在同目录 —— 跨卷移动不是原子操作，同卷才是。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// 以原子方式把 <paramref name="content"/> 写入 <paramref name="path"/>。
    /// </summary>
    /// <param name="path">目标文件路径（所在目录必须已存在）。</param>
    /// <param name="content">完整文件内容。</param>
    /// <param name="backup">是否先备份现有文件。默认开启。</param>
    /// <param name="keepBackups">保留的备份份数（仅 <paramref name="backup"/> 为真时有效）。</param>
    /// <returns>实际写入的字节数。</returns>
    /// <exception cref="IOException">临时文件写入失败或替换失败（此时原文件保持不变）。</exception>
    public static int Write(string path, string content, bool backup = true, int keepBackups = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // ① 先备份：这一步失败不能影响后续写入，但要留痕。
        //    宁可「写了新配置但没备份」，也不要「备份了却没写成功」——后者数据更新丢了。
        if (backup && File.Exists(path))
        {
            try
            {
                BackupService.Create(path, keepBackups);
            }
            catch (Exception ex)
            {
                Message.Yellow($"[原子写入] 备份 {Path.GetFileName(path)} 失败（继续写入）: {ex.Message}");
            }
        }

        // ② 写临时文件。带随机后缀，避免并发写入同一目标时互相踩踏。
        string tempPath = $"{path}{TempSuffix}-{Guid.NewGuid():N}";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                // 强制刷盘：不 Flush 的话数据可能还在系统缓存里，掉电就丢了。
                stream.Flush(true);
            }

            // ③ 原子替换。overwrite: true 在 NTFS 上等价于一次 rename，不会出现半截文件。
            File.Move(tempPath, path, overwrite: true);
            return bytes.Length;
        }
        catch
        {
            // 清理临时文件，避免留下垃圾；但不要让清理异常掩盖真正的错误。
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 原子写入的「字节版」，供二进制内容使用。语义同 <see cref="Write(string,string,bool,int)"/>。
    /// </summary>
    public static int WriteBytes(string path, byte[] bytes, bool backup = true, int keepBackups = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (backup && File.Exists(path))
        {
            try
            {
                BackupService.Create(path, keepBackups);
            }
            catch (Exception ex)
            {
                Message.Yellow($"[原子写入] 备份 {Path.GetFileName(path)} 失败（继续写入）: {ex.Message}");
            }
        }

        string tempPath = $"{path}{TempSuffix}-{Guid.NewGuid():N}";
        try
        {
            using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            File.Move(tempPath, path, overwrite: true);
            return bytes.Length;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 读取文件；文件不存在或内容不是合法 JSON 时，尝试回退到最近一份备份。
    ///
    /// <para>这是原子写入的「另一半」：写的一侧保证不产生坏文件，读的一侧保证
    /// 万一遇到坏文件（比如手工编辑改错了）不至于直接崩掉。</para>
    /// </summary>
    /// <param name="path">主文件路径。</param>
    /// <param name="validate">内容校验回调，返回 false 表示这份内容不可用。</param>
    /// <returns>可用的文件内容；主文件与全部备份都不可用时返回 null。</returns>
    public static string? ReadWithFallback(string path, Func<string, bool>? validate = null)
    {
        if (File.Exists(path))
        {
            string content = File.ReadAllText(path);
            if (validate is null || validate(content))
            {
                return content;
            }

            Message.Yellow($"[原子写入] {Path.GetFileName(path)} 内容校验失败，尝试回退到备份。");
        }

        foreach (string backup in BackupService.List(path))
        {
            try
            {
                string content = File.ReadAllText(backup);
                if (validate is null || validate(content))
                {
                    Message.Yellow($"[原子写入] 已回退到备份 {Path.GetFileName(backup)}。");
                    return content;
                }
            }
            catch (Exception ex)
            {
                Message.Yellow($"[原子写入] 读取备份 {Path.GetFileName(backup)} 失败: {ex.Message}");
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败无所谓，原异常更重要。
        }
    }
}
