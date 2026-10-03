using System.Collections.Concurrent;

namespace CaiBotWindy.Net;

/// <summary>
/// 临时文件托管：把内存中的字节落盘并生成一个短 ID，配合 <c>GET /download/{file_id}</c> 供 QQ 侧二次下载。
/// 默认保留 10 分钟（可配置）。
/// </summary>
public sealed class TempFileStore
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly string directory;

    public TempFileStore(string directory)
    {
        this.directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>落盘目录。注意不要在类内使用 <c>Directory</c> 作为成员名，否则会遮蔽 <see cref="System.IO.Directory"/>。</summary>
    public string DirectoryPath => directory;

    /// <summary>写入字节并返回可下载的 file_id。</summary>
    public string Add(byte[] data, string fileName)
    {
        string id = Guid.NewGuid().ToString("N");
        string safeName = SanitizeFileName(fileName);
        string path = Path.Combine(directory, $"{id}_{safeName}");
        File.WriteAllBytes(path, data);
        entries[id] = new Entry(path, fileName, DateTime.UtcNow);
        Cleanup();
        return id;
    }

    public string AddFile(string sourcePath)
    {
        return Add(File.ReadAllBytes(sourcePath), Path.GetFileName(sourcePath));
    }

    /// <summary>按 file_id 取回文件；不存在或已过期返回 <c>null</c>。</summary>
    public (string Path, string FileName)? Resolve(string id, TimeSpan lifetime)
    {
        if (!entries.TryGetValue(id, out Entry? entry))
        {
            return null;
        }

        if (DateTime.UtcNow - entry.CreatedAtUtc > lifetime || !File.Exists(entry.Path))
        {
            entries.TryRemove(id, out _);
            TryDelete(entry.Path);
            return null;
        }

        return (entry.Path, entry.FileName);
    }

    public void Cleanup()
    {
        foreach (KeyValuePair<string, Entry> item in entries.ToArray())
        {
            if (File.Exists(item.Value.Path))
            {
                continue;
            }

            entries.TryRemove(item.Key, out _);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "download.bin";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 文件已被占用时忽略，交给下次清理。
        }
    }

    private sealed record Entry(string Path, string FileName, DateTime CreatedAtUtc);
}
