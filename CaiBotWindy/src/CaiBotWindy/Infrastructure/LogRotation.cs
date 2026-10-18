using Windy.SDK;

namespace CaiBotWindy.Infrastructure;

/// <summary>
/// 运行日志轮转。
///
/// <para>Windy 的 <c>Message.WriteLog</c> 每次都是 <c>File.AppendAllText</c>（打开 → 写 → 关闭，
/// 不持有句柄），所以可以直接把超大的日志文件 rename 归档 —— 下一次写入会自然重建新文件。</para>
/// </summary>
public static class LogRotation
{
    /// <summary>超过该大小（字节）就归档。默认 32MB。</summary>
    public const long MaxBytes = 32 * 1024 * 1024;

    /// <summary>最多保留几份归档，超出的删最旧。</summary>
    public const int MaxArchives = 5;

    private static Timer? _timer;

    /// <summary>启动时立即检查一次，并挂一个每小时检查一次的定时器。</summary>
    public static void Start()
    {
        TryRotate();

        _timer ??= new Timer(_ => TryRotate(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    /// <summary>停止定时器（插件卸载时调用）。</summary>
    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>检查日志大小，超限则归档。失败静默（被占用时下次再试）。</summary>
    public static void TryRotate()
    {
        try
        {
            string file = Path.Combine(Message.LogDirectory, Message.LogFileName);
            if (!File.Exists(file) || new FileInfo(file).Length < MaxBytes)
            {
                return;
            }

            string stem = Path.GetFileNameWithoutExtension(Message.LogFileName);
            string ext = Path.GetExtension(Message.LogFileName);
            string archive = Path.Combine(
                Message.LogDirectory,
                $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

            File.Move(file, archive);
            PruneArchives(Message.LogDirectory, stem, ext);
        }
        catch
        {
            // 被并发写占用、或刚好已被轮转：下次定时器再试。
        }
    }

    private static void PruneArchives(string directory, string stem, string ext)
    {
        List<string> archives = Directory.GetFiles(directory, $"{stem}-*{ext}")
            .OrderByDescending(path => path)
            .ToList();

        foreach (string old in archives.Skip(MaxArchives))
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                // 删不掉就算了。
            }
        }
    }
}
