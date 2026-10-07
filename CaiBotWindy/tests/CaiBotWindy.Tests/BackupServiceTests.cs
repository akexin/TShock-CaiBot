using CaiBotWindy.Infrastructure;
using Xunit;

namespace CaiBotWindy.Tests;

/// <summary>
/// 自动备份的行为验证。
///
/// <para>重点测两件事：备份真的落下来了；以及备份<b>不会无限增长</b> ——
/// 后者是长期运行的隐患，store.json 每次进服校验都会写一次。</para>
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string directory;

    public BackupServiceTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "caibot-backup-" + Guid.NewGuid().ToString("N"));
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
            // 忽略清理失败。
        }
    }

    private string Full(string name) => Path.Combine(directory, name);

    [Fact]
    public void Create_ReturnsNull_WhenSourceMissing()
    {
        Assert.Null(BackupService.Create(Full("does-not-exist.json")));
    }

    [Fact]
    public void Create_CopiesContentVerbatim()
    {
        string file = Full("data.json");
        File.WriteAllText(file, "hello");

        string? backup = BackupService.Create(file);

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.Equal("hello", File.ReadAllText(backup));
    }

    [Fact]
    public void Create_PutsBackupsInSiblingBackupDirectory()
    {
        string file = Full("data.json");
        File.WriteAllText(file, "x");

        string? backup = BackupService.Create(file);

        Assert.NotNull(backup);
        // 备份落在同级 .backup 目录：同卷复制，出问题时也容易就地找回。
        Assert.Equal(".backup", new DirectoryInfo(Path.GetDirectoryName(backup)!).Name);
    }

    [Fact]
    public void Create_PruneKeepsOnlyMostRecent()
    {
        string file = Full("data.json");

        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(file, $"v{i}");
            BackupService.Create(file, keepCount: 2);
        }

        Assert.Equal(2, BackupService.List(file).Count);
    }

    [Fact]
    public void List_ReturnsNewestFirst()
    {
        string file = Full("data.json");

        File.WriteAllText(file, "v0");
        BackupService.Create(file);
        File.WriteAllText(file, "v1");
        BackupService.Create(file);

        List<string> backups = BackupService.List(file);
        Assert.Equal(2, backups.Count);
        Assert.Equal("v1", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void List_ReturnsEmpty_WhenNoBackups()
    {
        Assert.Empty(BackupService.List(Full("never-backed-up.json")));
    }

    [Fact]
    public void Clear_RemovesAllBackups()
    {
        string file = Full("data.json");
        File.WriteAllText(file, "x");
        BackupService.Create(file);
        BackupService.Create(file);

        int removed = BackupService.Clear(file);

        Assert.True(removed >= 1);
        Assert.Empty(BackupService.List(file));
    }
}
