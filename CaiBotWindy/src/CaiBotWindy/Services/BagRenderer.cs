using System.Text.Json;
using SkiaSharp;

namespace CaiBotWindy.Services;

/// <summary>背包卡片的一个容器分区（背包 / 虚空袋 / 钱罐 / 保险箱 / 防御者熔炉 / 装备…）。</summary>
public sealed class BagSection
{
    public required string Title { get; init; }

    public required int Columns { get; init; }

    /// <summary>至少画几行网格（背包固定留 3 行，避免新号看着像只有一排格子）；0 表示空容器不画网格。</summary>
    public required int MinRows { get; init; }

    /// <summary>标题左侧的容器图标（物品 id 字符串；空串表示不画）。</summary>
    public string IconItemId { get; init; } = "";

    /// <summary>该容器在 <c>inventory</c> 里对应的格子，保持原始顺序（含空位）。</summary>
    public List<(int ItemId, int Stack)> Slots { get; init; } = [];

    public int FilledCount
    {
        get
        {
            int count = 0;
            foreach ((int itemId, int stack) in Slots)
            {
                if (Resolve(itemId) > 0 && stack > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>最后一个有内容的格子所在行（0 基）；全空返回 -1。</summary>
    public int LastFilledRow
    {
        get
        {
            int last = -1;
            for (int index = 0; index < Slots.Count; index++)
            {
                if (Resolve(Slots[index].ItemId) > 0 && Slots[index].Stack > 0)
                {
                    last = index / Columns;
                }
            }

            return last;
        }
    }

    /// <summary>实际要画的网格行数。</summary>
    public int Rows => Math.Max(MinRows, LastFilledRow + 1);

    public static int Resolve(int itemId)
    {
        return BagRenderer.ResolveSlot(itemId);
    }
}

/// <summary>背包卡片渲染所需数据（对应 CaiBotLite <c>lookbag</c> 包的结构化字段）。</summary>
public sealed class BagSnapshot
{
    public string PlayerName { get; init; } = "";

    public string Life { get; init; } = "";

    public string Mana { get; init; } = "";

    public int QuestsCompleted { get; init; }

    /// <summary><c>lookbag</c> 包里的原始格子（在线玩家 350 格 / 离线玩家仅主背包）。</summary>
    public List<(int ItemId, int Stack)> Inventory { get; init; } = [];

    /// <summary>当前生效的增益（buff id）。</summary>
    public List<int> Buffs { get; init; } = [];

    /// <summary>永久增益（物品 id）。</summary>
    public List<int> Enhances { get; init; } = [];

    public string Coins { get; init; } = "";

    public string LevelName { get; init; } = "";

    public string Skill { get; init; } = "";

    /// <summary>切片后的容器分区。</summary>
    public List<BagSection> Sections { get; init; } = [];

    /// <summary>
    /// 是否拿到了完整格子。<c>LookOnline</c> 会把背包 / 装备 / 钱罐 / 保险箱 / 熔炉 / 虚空袋
    /// 拼成 350 格一次发过来；<c>LookOffline</c> 只发主背包 —— 这时银行类容器拿不到数据，
    /// 卡片上必须标注清楚，否则会被误读成「虚空袋是空的」。
    /// </summary>
    public bool HasFullInventory => Inventory.Count >= 260;
}

/// <summary>
/// 背包卡片渲染（<c>/查背包</c>）。
/// <para>官方 <c>lookbag.py</c> 把 350 格全部画进 1920×1080 的「游戏内背包界面」，缩到手机后字小到看不清；
/// 这里按<strong>容器分区</strong>重排：背包 / 虚空袋 / 钱罐 / 保险箱 / 防御者熔炉 / 装备与饰品 各占一块，
/// 每块只画到最后一个有内容的行（空容器压成一行提示），并且<strong>每个物品都标数量</strong>。</para>
/// </summary>
public static class BagRenderer
{
    /// <summary>卡片画布宽度 = 背景图原始宽度。</summary>
    public const int CardWidth = 976;

    /// <summary>卡片高度按实际内容动态计算，此值仅作兜底参考。</summary>
    public const int CardHeight = 844;

    private const string Background = "images/backgrounds/BagBackground.png";

    private const int Margin = 20;
    private const int Columns = 10;
    private const int CellSize = 86;
    private const int CellGap = 8;
    private const int RowHeight = CellSize + CellGap;
    private const int IconBox = 72;

    // 顶栏
    private const int HeaderBottom = 258;
    private const int BodyTop = 278;
    private const int SectionTitleHeight = 42;
    private const int EmptyHintHeight = 40;
    private const int SectionGap = 14;
    private const int FooterHeight = 56;

    /// <summary>
    /// 容器分区表。索引是 <c>lookbag</c> 包里 <c>inventory</c> 数组的下标，与 Terraria
    /// <c>NetItem.*Index</c> 常量一一对应（背包 0-58 / 装备染料宠物 59-98 / 钱罐 99-138 /
    /// 保险箱 139-178 / 垃圾桶 179 / 防御者熔炉 180-219 / 虚空袋 220-259 / 装填 260-349）。
    /// </summary>
    private static readonly (string Title, int Start, int Count, int MinRows, string Icon)[] SectionLayout =
    [
        ("背包", 0, 59, 3, ""),
        ("虚空袋", 220, 40, 0, "4131"),
        ("钱罐", 99, 40, 0, "87"),
        ("保险箱", 139, 40, 0, "88"),
        ("防御者熔炉", 180, 40, 0, "3813"),
        ("装备与饰品", 59, 40, 0, ""),
    ];

    // 背景是深色石室，面板必须够暗才能压住纹理、让图标与数字读得清
    private static readonly SKColor PanelColor = new(18, 24, 34, 214);
    private static readonly SKColor BodyPanelColor = new(18, 24, 34, 226);
    private static readonly SKColor PanelBorder = new(255, 255, 255, 48);
    private static readonly SKColor CellColor = new(255, 255, 255, 26);
    private static readonly SKColor TitleColor = new(255, 255, 255);
    private static readonly SKColor LifeColor = new(255, 132, 132);
    private static readonly SKColor ManaColor = new(122, 186, 255);
    private static readonly SKColor QuestColor = new(255, 214, 79);
    private static readonly SKColor DimColor = new(190, 200, 210);
    private static readonly SKColor CoinColor = new(255, 215, 0);
    private static readonly SKColor LevelColor = new(150, 205, 255);
    private static readonly SKColor SkillColor = new(198, 160, 255);
    private static readonly SKColor SubColor = new(215, 222, 230);
    private static readonly SKColor SectionColor = new(255, 226, 150);
    private static readonly SKColor EmptyColor = new(150, 160, 172);

    /// <summary>
    /// 背包里的「负 id」是特殊槽位，官方 <c>lookbag.py</c> 的 <c>NET_DEFAULTS</c> 把它们映射成真实物品 id。
    /// </summary>
    private static readonly Dictionary<int, int> NetDefaults = new()
    {
        [-1] = 3521, [-2] = 3520, [-3] = 3519, [-4] = 3518, [-5] = 3517, [-6] = 3516,
        [-7] = 3515, [-8] = 3514, [-9] = 3513, [-10] = 3512, [-11] = 3511, [-12] = 3510,
        [-13] = 3509, [-14] = 3508, [-15] = 3507, [-16] = 3506, [-17] = 3505, [-18] = 3504,
        [-19] = 3764, [-20] = 3765, [-21] = 3766, [-22] = 3767, [-23] = 3768, [-24] = 3769,
        [-25] = 3503, [-26] = 3502, [-27] = 3501, [-28] = 3500, [-29] = 3499, [-30] = 3498,
        [-31] = 3497, [-32] = 3496, [-33] = 3495, [-34] = 3494, [-35] = 3493, [-36] = 3492,
        [-37] = 3491, [-38] = 3490, [-39] = 3489, [-40] = 3488, [-41] = 3487, [-42] = 3486,
        [-43] = 3485, [-44] = 3484, [-45] = 3483, [-46] = 3482, [-47] = 3481, [-48] = 3480,
    };

    /// <summary>解析 <c>lookbag</c> 包 payload（也用于自测）。</summary>
    public static BagSnapshot FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<(int ItemId, int Stack)> inventory = [];
        if (root.TryGetProperty("inventory", out JsonElement rawInventory) && rawInventory.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement slot in rawInventory.EnumerateArray())
            {
                if (slot.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                int itemId = 0;
                int stack = 0;
                int index = 0;
                foreach (JsonElement field in slot.EnumerateArray())
                {
                    if (field.TryGetInt32(out int value))
                    {
                        if (index == 0)
                        {
                            itemId = value;
                        }
                        else if (index == 1)
                        {
                            stack = value;
                        }
                    }

                    index++;
                }

                inventory.Add((itemId, stack));
            }
        }

        string coins = "";
        string levelName = "";
        string skill = "";
        if (root.TryGetProperty("economic", out JsonElement economic) && economic.ValueKind == JsonValueKind.Object)
        {
            coins = ReadString(economic, "Coins");
            levelName = ReadString(economic, "LevelName");
            skill = ReadString(economic, "Skill");
        }

        BagSnapshot snapshot = new()
        {
            PlayerName = ReadString(root, "name"),
            Life = ReadString(root, "life"),
            Mana = ReadString(root, "mana"),
            QuestsCompleted = root.TryGetProperty("quests_completed", out JsonElement quests) && quests.TryGetInt32(out int completed)
                ? completed
                : 0,
            Inventory = inventory,
            Buffs = ReadIntArray(root, "buffs"),
            Enhances = ReadIntArray(root, "enhances"),
            Coins = coins,
            LevelName = levelName,
            Skill = skill,
        };

        // 按容器切片：数组长度不足时用 (0,0) 补齐，这样离线数据也能画出空的容器提示
        foreach ((string title, int start, int count, int minRows, string icon) in SectionLayout)
        {
            BagSection section = new()
            {
                Title = title,
                Columns = Columns,
                MinRows = minRows,
                IconItemId = icon,
            };

            for (int offset = 0; offset < count; offset++)
            {
                int index = start + offset;
                section.Slots.Add(index < inventory.Count ? inventory[index] : (0, 0));
            }

            snapshot.Sections.Add(section);
        }

        return snapshot;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => "",
        };
    }

    private static List<int> ReadIntArray(JsonElement element, string name)
    {
        List<int> result = [];
        if (element.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.TryGetInt32(out int value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    /// <summary>把背包槽位 id 归一成真实物品 id（处理 -1~-48 的特殊槽位）。</summary>
    public static int ResolveSlot(int itemId)
    {
        return itemId < 0 ? NetDefaults.GetValueOrDefault(itemId, 0) : itemId;
    }

    /// <summary>渲染背包卡片；返回 null 表示渲染不可用，调用方应退回文本。</summary>
    public static byte[]? Render(BagSnapshot snapshot)
    {
        if (!RenderKit.Ready || RenderKit.Typeface is null)
        {
            return null;
        }

        int height = Measure(snapshot);
        using SKBitmap? card = RenderKit.CreateCard(Background, CardWidth, height);
        if (card is null)
        {
            return null;
        }

        SKTypeface face = RenderKit.Typeface;
        using SKCanvas canvas = new(card);

        using TextStyle name = new(face, 46, TitleColor, shadow: true);
        using TextStyle info = new(face, 30, LifeColor, shadow: true);
        using TextStyle infoAlt = new(face, 30, ManaColor, shadow: true);
        using TextStyle quest = new(face, 30, QuestColor, shadow: true);
        using TextStyle label = new(face, 26, SubColor, shadow: true);
        using TextStyle small = new(face, 22, DimColor, shadow: true);
        using TextStyle coin = new(face, 26, CoinColor, shadow: true);
        using TextStyle level = new(face, 26, LevelColor, shadow: true);
        using TextStyle skill = new(face, 26, SkillColor, shadow: true);
        using TextStyle stack = new(face, 24, TitleColor, shadow: true);
        using TextStyle sectionTitle = new(face, 28, SectionColor, shadow: true);
        using TextStyle sectionCount = new(face, 22, SubColor, shadow: true);
        using TextStyle sectionEmpty = new(face, 22, EmptyColor, shadow: true);

        // 顶栏面板
        RenderKit.Panel(canvas, new SKRect(10, 8, CardWidth - 10, HeaderBottom), 14, PanelColor, PanelBorder);

        // 内容区统一底色：分区标题、空容器提示都是直接画文字，
        // 没有这层底会压到背景的火把/木箱上，读不清。
        float bodyHeight = MeasureBody(snapshot);
        RenderKit.Panel(
            canvas,
            new SKRect(10, BodyTop - 12, CardWidth - 10, BodyTop + bodyHeight),
            14,
            BodyPanelColor,
            PanelBorder);

        DrawHeader(canvas, name, small, snapshot);
        DrawInfo(canvas, info, infoAlt, quest, label, coin, level, skill, snapshot);

        float y = BodyTop;
        foreach (BagSection section in snapshot.Sections)
        {
            y = DrawSection(canvas, section, y, sectionTitle, sectionCount, sectionEmpty, stack);
        }

        DrawFooter(canvas, small, snapshot, y);

        return RenderKit.Encode(card);
    }

    /// <summary>先量高度再建画布（高度由实际内容决定）。</summary>
    private static int Measure(BagSnapshot snapshot)
    {
        return (int)MathF.Ceiling(BodyTop + MeasureBody(snapshot)) + FooterHeight;
    }

    /// <summary>内容区总高度（不含顶栏与底栏）。</summary>
    private static float MeasureBody(BagSnapshot snapshot)
    {
        float y = 0;
        foreach (BagSection section in snapshot.Sections)
        {
            y += SectionTitleHeight;
            y += section.Rows > 0 ? section.Rows * RowHeight : EmptyHintHeight;
            y += SectionGap;
        }

        return y;
    }

    private static void DrawHeader(SKCanvas canvas, TextStyle name, TextStyle small, BagSnapshot snapshot)
    {
        string title = string.IsNullOrWhiteSpace(snapshot.PlayerName) ? "背包" : $"背包 · {snapshot.PlayerName}";
        name.Draw(canvas, title, Margin + 4, 16);

        int total = 0;
        foreach (BagSection section in snapshot.Sections)
        {
            total += section.FilledCount;
        }

        small.DrawRight(canvas, $"共 {total} 件", CardWidth - Margin - 4, 32);
    }

    private static void DrawInfo(
        SKCanvas canvas,
        TextStyle life,
        TextStyle mana,
        TextStyle quest,
        TextStyle label,
        TextStyle coin,
        TextStyle level,
        TextStyle skill,
        BagSnapshot snapshot)
    {
        float left = Margin + 8;
        float right = (CardWidth / 2f) + 8;

        const int RowStep = 32;
        const float InfoTop = 82;

        life.Draw(canvas, $"生命 {Value(snapshot.Life, "未知")}", left, InfoTop);
        mana.Draw(canvas, $"魔力 {Value(snapshot.Mana, "未知")}", left, InfoTop + RowStep);
        quest.Draw(canvas, $"渔夫任务 {snapshot.QuestsCompleted} 次", left, InfoTop + (RowStep * 2));

        bool hasCoins = !string.IsNullOrWhiteSpace(snapshot.Coins);
        bool hasLevel = !string.IsNullOrWhiteSpace(snapshot.LevelName);
        bool hasSkill = !string.IsNullOrWhiteSpace(snapshot.Skill);
        if (hasCoins || hasLevel || hasSkill)
        {
            label.Draw(canvas, "经济", right, InfoTop);
            float y = InfoTop + RowStep;
            if (hasCoins)
            {
                coin.Draw(canvas, Compact(snapshot.Coins), right, y);
                y += RowStep;
            }

            if (hasLevel)
            {
                level.Draw(canvas, Compact(snapshot.LevelName), right, y);
                y += RowStep;
            }

            if (hasSkill)
            {
                skill.Draw(canvas, Compact(snapshot.Skill), right, y);
            }
        }

        // 永久增益 / 增益图标行（左列三行信息下方的空档里，留 6px 躲开右列第四行「技能」）
        float iconY = InfoTop + (RowStep * 4) + 6;
        float x = DrawIconRow(canvas, "永久增益", left, iconY, snapshot.Enhances, "images/items/Item_{0}.png", 40);
        DrawIconRow(canvas, "增益", x + 24, iconY, snapshot.Buffs, "images/buffs/Buff_{0}.png", 40);
    }

    /// <summary>画一行「标签 + 图标」，返回该行结束时的 x 坐标。</summary>
    private static float DrawIconRow(SKCanvas canvas, string title, float x, float y, List<int> ids, string template, int size)
    {
        List<int> valid = [.. ids.Where(id => id > 0)];
        if (valid.Count == 0)
        {
            return x;
        }

        using TextStyle face = new(RenderKit.Typeface!, 26, SubColor, shadow: true);
        face.Draw(canvas, title, x, y);
        float cursor = x + face.Width(title) + 10;

        foreach (int id in valid.Take(14))
        {
            if (cursor + size > CardWidth - Margin - 8)
            {
                break;
            }

            SKBitmap? icon = RenderKit.Asset(string.Format(template, id));
            if (icon is null)
            {
                continue;
            }

            using SKBitmap scaled = RenderKit.Fit(icon, size, size);
            RenderKit.DrawBitmap(canvas, scaled, cursor, y - 4);
            cursor += size + 4;
        }

        return cursor;
    }

    /// <summary>画一个容器分区，返回下一个分区可用的 y 坐标。</summary>
    private static float DrawSection(
        SKCanvas canvas,
        BagSection section,
        float top,
        TextStyle title,
        TextStyle count,
        TextStyle empty,
        TextStyle stack)
    {
        float cursor = top;

        // 标题行：[容器图标] 名称 · N 件
        float textX = Margin + 4;
        if (section.IconItemId.Length > 0)
        {
            SKBitmap? icon = RenderKit.Asset($"images/items/Item_{section.IconItemId}.png");
            if (icon is not null)
            {
                using SKBitmap scaled = RenderKit.Fit(icon, 30, 30, allowUpscale: true);
                RenderKit.DrawBitmap(canvas, scaled, textX, cursor + 2);
                textX += scaled.Width + 8;
            }
        }

        title.Draw(canvas, section.Title, textX, cursor);
        textX += title.Width(section.Title) + 12;

        count.Draw(canvas, section.FilledCount > 0 ? $"{section.FilledCount} 件" : "空", textX, cursor + 5);

        using (SKPaint line = new() { Color = new SKColor(255, 255, 255, 40), StrokeWidth = 1, IsAntialias = true })
        {
            canvas.DrawLine(
                Margin + 4,
                cursor + SectionTitleHeight - 8,
                CardWidth - Margin - 4,
                cursor + SectionTitleHeight - 8,
                line);
        }

        cursor += SectionTitleHeight;

        if (section.Rows <= 0)
        {
            // 空容器：压成一行提示，省掉整片空格子
            empty.Draw(canvas, $"（没有存放任何物品，共 {section.Slots.Count} 格）", Margin + 8, cursor + 2);
            return cursor + EmptyHintHeight + SectionGap;
        }

        int rows = section.Rows;
        float gridTop = cursor;
        float gridHeight = rows * RowHeight;

        // 分区不再单独铺面板：内容区已有一层统一深色底，再叠一层会显得厚重
        int startX = (CardWidth - ((Columns * CellSize) + ((Columns - 1) * CellGap))) / 2;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = (row * Columns) + column;
                float cellX = startX + (column * (CellSize + CellGap));
                float cellY = gridTop + (row * RowHeight);

                // 空槽也画格子：整体呈现为一个完整的容器网格，比「画到哪算哪」整齐
                RenderKit.Panel(
                    canvas,
                    new SKRect(cellX, cellY, cellX + CellSize, cellY + CellSize),
                    10,
                    CellColor);

                if (index >= section.Slots.Count)
                {
                    continue;
                }

                (int rawId, int stackCount) = section.Slots[index];
                int itemId = BagSection.Resolve(rawId);
                if (itemId <= 0 || stackCount <= 0)
                {
                    continue;
                }

                SKBitmap? item = RenderKit.Asset($"images/items/Item_{itemId}.png");
                if (item is null)
                {
                    continue;
                }

                using SKBitmap scaled = RenderKit.Fit(item, IconBox, IconBox, allowUpscale: true);
                RenderKit.DrawBitmap(
                    canvas,
                    scaled,
                    cellX + ((CellSize - scaled.Width) / 2f),
                    cellY + ((CellSize - scaled.Height) / 2f) - 4);

                // 数量全部显示（官方只在 >1 时显示，但玩家看不到 1 会以为「没有数量」）
                stack.DrawRight(canvas, stackCount.ToString(), cellX + CellSize - 5, cellY + CellSize - 32);
            }
        }

        return gridTop + gridHeight + SectionGap;
    }

    private static void DrawFooter(SKCanvas canvas, TextStyle small, BagSnapshot snapshot, float top)
    {
        float y = top + 8;
        small.Draw(canvas, "By ak", Margin + 4, y);
        small.Draw(canvas, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Margin + 78, y);

        if (!snapshot.HasFullInventory)
        {
            // 离线玩家只发主背包，银行类容器拿不到数据 —— 必须说明，否则会被当成「虚空袋是空的」
            small.DrawRight(canvas, "离线玩家存档：仅主背包可用", CardWidth - Margin - 4, y);
        }
    }

    private static string Value(string text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    /// <summary>经济数据可能很长（多行），压成一行避免撑破卡片。</summary>
    private static string Compact(string text)
    {
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (flat.Contains("  "))
        {
            flat = flat.Replace("  ", " ");
        }

        return flat.Length > 26 ? flat[..26] + "…" : flat;
    }
}
