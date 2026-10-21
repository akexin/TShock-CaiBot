using System.IO.Compression;
using System.Text;
using CaiBotWindy.Commands;
using CaiBotWindy.Services;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 跨端日志镜像的本地部分：日志时间戳解析 与 zip 解包。
///
/// <para>远端交互（<c>server_file</c> 的 info / view / download）复用 <c>/发文件</c> 已经上线验证过的
/// 同一套请求-响应链路，这里不重复造 WebSocket；真正容易出错的是「解压时的路径安全」和
/// 「mtime 不可靠时如何排序」这两处纯本地逻辑。</para>
/// </summary>
public sealed class RemoteLogTests
{
    [Fact]
    public void LogTime_UsesTimestampInFileName_NotWriteTime()
    {
        // 跨端镜像解压后 mtime 会变成打包时刻，只有文件名里的时间戳是可靠的。
        using TempDirectory temp = new();
        FileInfo file = temp.Create("2026-10-21_23-41-31.log", "x");
        file.LastWriteTime = new DateTime(2000, 1, 1, 0, 0, 0);

        Assert.Equal(new DateTime(2026, 10, 21, 23, 41, 31), LogCommands.LogTime(file));
    }

    [Fact]
    public void LogTime_FallsBackToWriteTime_WhenNameHasNoTimestamp()
    {
        using TempDirectory temp = new();
        FileInfo file = temp.Create("latest.log", "x");
        DateTime expected = new(2026, 5, 6, 7, 8, 9);
        file.LastWriteTime = expected;

        Assert.Equal(expected, LogCommands.LogTime(file));
    }

    [Fact]
    public void Extract_WritesEntries_AndDropsLogsMissingFromArchive()
    {
        using TempDirectory temp = new();
        string cacheDir = Path.Combine(temp.Path, "mirror");
        Directory.CreateDirectory(cacheDir);

        // 上一轮残留的旧日志：远端已经轮转掉了，必须被清掉。
        File.WriteAllText(Path.Combine(cacheDir, "old.log"), "stale");

        byte[] archive = BuildZip(("2026-10-21_10-00-00.log", "hello"), ("2026-10-21_11-00-00.log", "world"));

        int count = RemoteLogMirror.Extract(archive, cacheDir);

        Assert.Equal(2, count);
        Assert.False(File.Exists(Path.Combine(cacheDir, "old.log")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(cacheDir, "2026-10-21_10-00-00.log")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(cacheDir, "2026-10-21_11-00-00.log")));
    }

    [Fact]
    public void Extract_SkipsEntriesThatEscapeTheCacheDirectory()
    {
        using TempDirectory temp = new();
        string cacheDir = Path.Combine(temp.Path, "mirror");
        Directory.CreateDirectory(cacheDir);

        byte[] archive = BuildZip(("../escaped.log", "pwned"), ("2026-10-21_12-00-00.log", "ok"));

        int count = RemoteLogMirror.Extract(archive, cacheDir);

        Assert.Equal(1, count);
        Assert.False(File.Exists(Path.Combine(temp.Path, "escaped.log")));
        Assert.True(File.Exists(Path.Combine(cacheDir, "2026-10-21_12-00-00.log")));
    }

    [Fact]
    public void Extract_KeepsNestedLayout_ForSubDirectories()
    {
        using TempDirectory temp = new();
        string cacheDir = Path.Combine(temp.Path, "mirror");

        byte[] archive = BuildZip(("archive/2026-10-21_13-00-00.log", "nested"));

        Assert.Equal(1, RemoteLogMirror.Extract(archive, cacheDir));
        Assert.True(File.Exists(Path.Combine(cacheDir, "archive", "2026-10-21_13-00-00.log")));
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>用完即删的临时目录。</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cbot-log-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public FileInfo Create(string name, string content)
        {
            string full = System.IO.Path.Combine(Path, name);
            File.WriteAllText(full, content);
            return new FileInfo(full);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // 临时目录清不掉不影响断言结果。
            }
        }
    }
}
