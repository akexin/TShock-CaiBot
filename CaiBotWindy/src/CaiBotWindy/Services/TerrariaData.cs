using Newtonsoft.Json;

namespace CaiBotWindy.Services;

/// <summary>货币价值（铜 / 银 / 金 / 铂）。</summary>
public sealed class CoinValue
{
    public int Copper;
    public int Silver;
    public int Gold;
    public int Platinum;

    public bool HasValue()
    {
        return Copper > 0 || Silver > 0 || Gold > 0 || Platinum > 0;
    }

    public string Format()
    {
        if (!HasValue())
        {
            return "无价之宝";
        }

        List<string> parts = [];
        if (Platinum > 0) parts.Add($"{Platinum}铂");
        if (Gold > 0) parts.Add($"{Gold}金");
        if (Silver > 0) parts.Add($"{Silver}银");
        if (Copper > 0) parts.Add($"{Copper}铜");
        return string.Join(" ", parts);
    }
}

/// <summary>物品图鉴条目。</summary>
public sealed class ItemInfo
{
    public int ItemId;
    public string Name = "";
    public int MaxStack;
    public int Damage;
    public int Shoot;
    public int Crit;
    public string Description = "";
    public CoinValue MonetaryValue = new();
    public List<string> Alias = [];
}

/// <summary>生物（NPC）图鉴条目。</summary>
public sealed class NpcInfo
{
    public int NpcId;
    public string Name = "";
    public int LifeMax;
    public int Damage;
    public string Description = "";
    public CoinValue MonetaryValue = new();
    public List<string> Alias = [];
}

/// <summary>弹幕（射弹）图鉴条目。</summary>
public sealed class ProjectInfo
{
    public int ProjId;
    public string Name = "";
    public int AiStyle;
    public bool Friendly;
    public List<string> Alias = [];
}

/// <summary>增益（Buff）图鉴条目。</summary>
public sealed class BuffInfo
{
    public int BuffId;
    public string Name = "";
    public string Description = "";
    public List<string> Alias = [];
}

/// <summary>修饰语（前缀）图鉴条目。</summary>
public sealed class PrefixInfo
{
    public int PrefixId;
    public string Name = "";
    public List<string> Alias = [];
}

/// <summary>
/// 图鉴数据索引。数据文件来自 UnrealMultiple/TerrariaID，放在 <c>Data/</c> 目录下：
/// <c>item_id.json</c> / <c>npc_id.json</c> / <c>project_id.json</c> / <c>buff_id.json</c> / <c>prefix_id.json</c>。
/// </summary>
public static class TerrariaData
{
    private static string dataDirectory = "";

    public static List<ItemInfo> Items { get; private set; } = [];

    public static List<NpcInfo> Npcs { get; private set; } = [];

    public static List<ProjectInfo> Projects { get; private set; } = [];

    public static List<BuffInfo> Buffs { get; private set; } = [];

    public static List<PrefixInfo> Prefixes { get; private set; } = [];

    public static bool IsLoaded => Items.Count > 0;

    public static string DataDirectory => dataDirectory;

    public static void Load(string directory)
    {
        dataDirectory = directory;
        Directory.CreateDirectory(directory);

        Items = LoadFile<ItemInfo>("item_id.json");
        Npcs = LoadFile<NpcInfo>("npc_id.json");
        Projects = LoadFile<ProjectInfo>("project_id.json");
        Buffs = LoadFile<BuffInfo>("buff_id.json");
        Prefixes = LoadFile<PrefixInfo>("prefix_id.json");

        Windy.SDK.Message.Green(
            $"[CaiBotWindy] 图鉴数据已加载: 物品 {Items.Count} / 生物 {Npcs.Count} / " +
            $"弹幕 {Projects.Count} / 增益 {Buffs.Count} / 前缀 {Prefixes.Count}");
    }

    private static List<T> LoadFile<T>(string fileName)
    {
        string path = Path.Combine(dataDirectory, fileName);
        if (!File.Exists(path))
        {
            Windy.SDK.Message.Yellow($"[CaiBotWindy] 缺少图鉴数据文件: {path}（对应搜索指令将不可用）");
            return [];
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<List<T>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Windy.SDK.Message.Red($"[CaiBotWindy] 读取 {fileName} 失败: {ex.Message}");
            return [];
        }
    }

    // ── 物品 ────────────────────────────────────────────────────────────────────

    public static ItemInfo? GetItemById(int id)
    {
        return Items.FirstOrDefault(item => item.ItemId == id);
    }

    public static List<ItemInfo> SearchItems(string query)
    {
        return Search(Items, query, item => item.ItemId, item => item.Name, item => item.Alias);
    }

    public static string ProjectileName(int projectileId)
    {
        ProjectInfo? projectile = Projects.FirstOrDefault(item => item.ProjId == projectileId);
        return projectile?.Name ?? projectileId.ToString();
    }

    // ── 生物 ────────────────────────────────────────────────────────────────────

    public static NpcInfo? GetNpcById(int id)
    {
        return Npcs.FirstOrDefault(item => item.NpcId == id);
    }

    public static List<NpcInfo> SearchNpcs(string query)
    {
        return Search(Npcs, query, item => item.NpcId, item => item.Name, item => item.Alias);
    }

    // ── 弹幕 ────────────────────────────────────────────────────────────────────

    public static ProjectInfo? GetProjectileById(int id)
    {
        return Projects.FirstOrDefault(item => item.ProjId == id);
    }

    public static List<ProjectInfo> SearchProjectiles(string query)
    {
        return Search(Projects, query, item => item.ProjId, item => item.Name, item => item.Alias);
    }

    // ── 增益 ────────────────────────────────────────────────────────────────────

    public static BuffInfo? GetBuffById(int id)
    {
        return Buffs.FirstOrDefault(item => item.BuffId == id);
    }

    public static List<BuffInfo> SearchBuffs(string query)
    {
        return Search(Buffs, query, item => item.BuffId, item => item.Name, item => item.Alias);
    }

    // ── 前缀 ────────────────────────────────────────────────────────────────────

    public static PrefixInfo? GetPrefixById(int id)
    {
        return Prefixes.FirstOrDefault(item => item.PrefixId == id);
    }

    public static List<PrefixInfo> SearchPrefixes(string query)
    {
        return Search(Prefixes, query, item => item.PrefixId, item => item.Name, item => item.Alias);
    }

    // ── 检索实现 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 查询策略：ID 精确匹配 → 名称 / 别名精确 → 包含 → 模糊。返回按相关度降序的结果。
    /// </summary>
    private static List<T> Search<T>(
        List<T> source,
        string query,
        Func<T, int> idSelector,
        Func<T, string> nameSelector,
        Func<T, IReadOnlyList<string>> aliasSelector)
    {
        if (source.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        query = query.Trim();

        if (int.TryParse(query, out int numericId))
        {
            List<T> byId = source.Where(item => idSelector(item) == numericId).ToList();
            if (byId.Count > 0)
            {
                return byId;
            }
        }

        string lowered = query.ToLowerInvariant();

        List<(T Item, int Score)> scored = [];
        foreach (T item in source)
        {
            string name = nameSelector(item) ?? "";
            int score = ScoreName(lowered, name, exact: 1000, prefix: 700, contains: 400);

            foreach (string alias in aliasSelector(item) ?? [])
            {
                score = Math.Max(score, ScoreName(lowered, alias, exact: 950, prefix: 650, contains: 380));
            }

            if (score <= 0 && lowered.Length >= 2)
            {
                // 兜底模糊匹配：仅在长度接近时计算，避免全表 O(n·m) 的昂贵比较。
                string lowerName = name.ToLowerInvariant();
                if (Math.Abs(lowerName.Length - lowered.Length) <= 3)
                {
                    int distance = Levenshtein(lowered, lowerName);
                    int maxLength = Math.Max(lowered.Length, lowerName.Length);
                    double similarity = maxLength == 0 ? 0 : 1.0 - (double)distance / maxLength;
                    if (similarity >= 0.62)
                    {
                        score = (int)(similarity * 300);
                    }
                }
            }

            if (score > 0)
            {
                scored.Add((item, score));
            }
        }

        return scored
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => nameSelector(pair.Item), StringComparer.Ordinal)
            .Select(pair => pair.Item)
            .ToList();
    }

    private static int ScoreName(string loweredQuery, string candidate, int exact, int prefix, int contains)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return 0;
        }

        string lowerCandidate = candidate.ToLowerInvariant();
        if (lowerCandidate == loweredQuery)
        {
            return exact;
        }

        if (lowerCandidate.StartsWith(loweredQuery, StringComparison.Ordinal))
        {
            return prefix - Math.Min(100, lowerCandidate.Length - loweredQuery.Length);
        }

        int index = lowerCandidate.IndexOf(loweredQuery, StringComparison.Ordinal);
        if (index >= 0)
        {
            return Math.Max(1, contains - index * 10 - Math.Min(50, lowerCandidate.Length - loweredQuery.Length));
        }

        return 0;
    }

    private static int Levenshtein(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
