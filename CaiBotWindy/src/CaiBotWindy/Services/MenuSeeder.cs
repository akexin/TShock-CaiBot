namespace CaiBotWindy.Services;

/// <summary>
/// 把随插件分发的菜单配置补种到运行目录。
/// <para><c>/菜单面板</c> 需要运行目录下的 <c>Menu\menu.json</c> 与 <c>Menu\panels.json</c>，
/// 但部署搬运时容易漏掉整个 <c>Menu\</c> 目录。这里以编译期内嵌的资源作兜底，
/// 只补缺失（或空）的文件，不覆盖已存在的内容。</para>
/// </summary>
public static class MenuSeeder
{
    private static readonly (string FileName, string ResourceName)[] Files =
    [
        ("menu.json", "CaiBotWindy.Menu.menu.json"),
        ("panels.json", "CaiBotWindy.Menu.panels.json"),
    ];

    /// <summary>确保菜单目录存在且两个配置文件齐备，返回本次补种的文件数。</summary>
    public static int Ensure(string menuDirectory)
    {
        int seeded = 0;

        foreach ((string fileName, string resourceName) in Files)
        {
            string target = Path.Combine(menuDirectory, fileName);
            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                continue;
            }

            Stream? source = typeof(MenuSeeder).Assembly.GetManifestResourceStream(resourceName);
            if (source is null)
            {
                continue;
            }

            Directory.CreateDirectory(menuDirectory);

            using (source)
            using (FileStream output = File.Create(target))
            {
                source.CopyTo(output);
            }

            seeded++;
        }

        return seeded;
    }
}
