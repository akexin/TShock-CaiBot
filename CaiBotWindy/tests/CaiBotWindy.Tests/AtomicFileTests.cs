using CaiBotWindy.Infrastructure;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 原子写入的行为验证。
///
/// <para>这一块值得单独测：配置损坏最要命的后果是「机器人下一次起不来」，
/// 而这类 bug 平时完全看不出来，只有在进程被强杀 / 磁盘写满时才暴露。</para>
/// </summary>
public class AtomicFileTests : IDisposable
{
    private readonly string directory;

    public AtomicFileTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "caibot-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    private string Full(string name) => Path.Combine(directory, name);

    [Fact]
    public void Write_CreatesFileWithExpectedContent()
    {
        string file = Full("created.json");

        AtomicFile.Write(file, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(file));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        string file = Full("overwrite.json");

        AtomicFile.Write(file, "first");
        AtomicFile.Write(file, "second");

        Assert.Equal("second", File.ReadAllText(file));
    }

    [Fact]
    public void Write_LeavesNoTempFilesBehind()
    {
        string file = Full("clean.json");

        AtomicFile.Write(file, "content");

        // 临时文件必须被清理，否则长期运行会在配置目录里堆一堆 .tmp。
        string[] leftovers = Directory.GetFiles(directory, "*.tmp-*");
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Write_BackupsPreviousContent()
    {
        string file = Full("backup.json");

        AtomicFile.Write(file, "v1");
        AtomicFile.Write(file, "v2");

        Assert.Equal("v2", File.ReadAllText(file));

        List<string> backups = BackupService.List(file);
        Assert.Single(backups);
        Assert.Equal("v1", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void ReadWithFallback_ReturnsMainContent_WhenValid()
    {
        string file = Full("valid.json");
        File.WriteAllText(file, "{\"ok\":true}");

        Assert.Equal("{\"ok\":true}", AtomicFile.ReadWithFallback(file, IsJson));
    }

    [Fact]
    public void ReadWithFallback_UsesBackup_WhenMainIsCorrupt()
    {
        string file = Full("corrupt.json");

        // 第一次写入时文件还不存在，没有「旧内容」可备份；
        // 第二次写入才会把第一版存进备份 —— 回退拿到的正是这一版。
        AtomicFile.Write(file, "{\"version\":1}");
        AtomicFile.Write(file, "{\"version\":2}");
        File.WriteAllText(file, "{ 这不是合法 JSON");

        // 主文件坏了，应当自动回退到备份，而不是把坏内容交给调用方。
        Assert.Equal("{\"version\":1}", AtomicFile.ReadWithFallback(file, IsJson));
    }

    [Fact]
    public void ReadWithFallback_ReturnsNull_WhenNothingIsUsable()
    {
        string file = Full("hopeless.json");
        File.WriteAllText(file, "坏内容");

        Assert.Null(AtomicFile.ReadWithFallback(file, IsJson));
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        string file = Full(Path.Combine("nested", "deep", "file.json"));

        AtomicFile.Write(file, "ok");

        Assert.True(File.Exists(file));
    }

    private static bool IsJson(string content)
    {
        try
        {
            Newtonsoft.Json.Linq.JObject.Parse(content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
