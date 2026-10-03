using System.Text.Json;
using SkiaSharp;
using Windy.SDK;

namespace CaiBotWindy.Services;

/// <summary>进度卡片渲染所需的数据（对应 CaiBotLite <c>progress</c> 包的结构化字段）。</summary>
public sealed class ProgressSnapshot
{
    public string WorldName { get; init; } = "";

    public string WorldIcon { get; init; } = "";

    public bool DrunkWorld { get; init; }

    public bool ZenithWorld { get; init; }

    public Dictionary<string, bool> Process { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> KillCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> BossLock { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 世界进度卡片渲染。Boss 组合沿用官方 CaiBotLite <c>services/query_process.py</c>，
/// 底图换成自绘的夜色星空 <c>ProgressBackground.png</c>（3:4），版式也完全重做过。
/// <para><b>为什么是竖版 1080×1440</b>：官方是 1920×1080 的 16:9 横图，缩到手机 QQ 聊天窗口的
/// 缩略宽度（约 240pt）后只剩 240×135pt，Boss 标签的实际字号不到 6pt，糊成一团读不了。
/// 改成 3:4 竖版后同样的 240pt 宽能换来 240×320pt 的显示面积，配合同样放大的字号，
/// 标签在聊天窗口里不点开也能读清。</para>
/// <list type="bullet">
///   <item>字号占画布宽的比例由 0.024 提到 0.048（正文 44 / Boss 标签 52），缩略后约 11.5pt；</item>
///   <item>入侵事件由官方「单列 24px 行距」（相邻行互相压字）改为<strong>两列 52px 行距</strong>；</item>
///   <item>Boss 由官方「6 列自由坐标挤排」改为 <strong>3 列 × 7 行网格</strong>，图标底对齐、标签居中；</item>
///   <item>底图统一为暗色星空（均值亮度约 27），固定用<strong>浅色字 + 8 方向描边</strong>通吃三个世界；
///         官方三张 1920×1080 横底图裁成 3:4 后左右各丢约 29% 画面，且亮度从 41 到 187 跨度极大。</item>
/// </list>
/// </summary>
public static class ProgressRenderer
{
    private const int CardWidth = 1080;
    private const int CardHeight = 1440;

    // 背景：AI 生成的夜色星空（1080×1440，3:4），普通 / 醉酒 / 天顶三个世界共用一张。
    // 原始产物留在 CaiBotWindy/assets/ProgressBackground.source.png，处理脚本
    // CaiBotWindy/scripts/prepare_card_background.py（裁掉底部水印后裁 3:4）。
    private const string BackgroundScene = "images/backgrounds/ProgressBackground.png";
    private const string LockItemIcon = "images/items/Item_5328.png";

    // ── 版式常量（微调改这里）────────────────────────────────────────────────
    private const int Margin = 40;

    // 标题区
    private const int TitleTop = 10;
    private const int HeadingTop = 96;
    private const int StatTop = 110;
    private const int SignatureTop = 18;

    // 入侵事件区：6 条排两列，四柱单独通栏
    private const int EventTop = 186;
    private const int EventRowHeight = 52;
    private const int EventColumnLeft = 40;
    private const int EventColumnRight = 548;
    private const int EventIconBox = 38;
    private const int PillarsTop = EventTop + (3 * EventRowHeight);

    // Boss 网格：3 列 × 7 行（20 个 Boss + 1 个统计徽章）
    private const int GridLeft = 40;
    private const int GridTop = 410;
    private const int GridColumns = 3;
    private const int GridColumnWidth = 333;
    private const int GridRowHeight = 146;
    private const int IconBoxWidth = 260;
    private const int IconBoxHeight = 72;

    private static readonly string[] NormalBosses =
    [
        "King Slime", "Eye of Cthulhu", "Eater of Worlds", "Brain of Cthulhu", "Queen Bee",
        "Deerclops", "Skeletron", "Wall of Flesh", "Queen Slime", "The Destroyer",
        "The Twins", "Skeletron Prime", "Plantera", "Golem", "Duke Fishron",
        "Empress of Light", "Lunatic Cultist", "Moon Lord",
    ];

    private static readonly string[] ZenithBosses =
    [
        "King Slime", "Eye of Cthulhu", "Eater of Worlds", "Brain of Cthulhu", "Queen Bee",
        "Deerclops", "Skeletron", "Wall of Flesh", "Queen Slime", "Mechdusa",
        "Plantera", "Golem", "Duke Fishron", "Empress of Light", "Lunatic Cultist",
        "Moon Lord",
    ];

    /// <summary>返回不可用原因；可用时为 null（转发 <see cref="RenderKit"/>）。</summary>
    public static string? UnavailableReason => RenderKit.UnavailableReason;

    /// <summary>把 CaiBotLite <c>progress</c> 包的 payload 解析成渲染快照（也用于自测）。</summary>
    public static ProgressSnapshot FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ProgressSnapshot snapshot = new()
        {
            WorldName = GetString(root, "world_name"),
            WorldIcon = GetString(root, "world_icon"),
            DrunkWorld = GetBool(root, "drunk_world"),
            ZenithWorld = GetBool(root, "zenith_world"),
        };

        if (root.TryGetProperty("process", out JsonElement process) && process.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in process.EnumerateObject())
            {
                snapshot.Process[entry.Name] = entry.Value.ValueKind == JsonValueKind.True;
            }
        }

        if (root.TryGetProperty("kill_counts", out JsonElement kills) && kills.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in kills.EnumerateObject())
            {
                if (entry.Value.TryGetInt32(out int count))
                {
                    snapshot.KillCounts[entry.Name] = count;
                }
            }
        }

        if (root.TryGetProperty("boss_lock", out JsonElement locks) && locks.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in locks.EnumerateObject())
            {
                snapshot.BossLock[entry.Name] = entry.Value.GetString() ?? "";
            }
        }

        return snapshot;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    /// <summary>渲染进度卡片；返回 null 表示渲染不可用，调用方应退回文本输出。</summary>
    public static byte[]? Render(ProgressSnapshot snapshot)
    {
        if (!RenderKit.Ready || RenderKit.Typeface is null)
        {
            return null;
        }

        using SKBitmap? card = RenderKit.CreateCard(BackgroundScene, CardWidth, CardHeight);
        if (card is null)
        {
            return null;
        }

        try
        {
            using DrawJob job = new(card, RenderKit.Typeface, snapshot);
            job.Run();
            return RenderKit.Encode(card);
        }
        catch (Exception ex)
        {
            Message.Red($"[ProgressRenderer] 渲染失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 卡片配色。底图统一为暗色星空，所以固定一套浅色字 + 描边即可。
    /// <para>原先要按世界类型切两套（天顶背景很暗得用浅色字、其余用黑字），换成统一底图后不再需要。</para>
    /// </summary>
    private sealed class Palette
    {
        public required SKColor Text { get; init; }

        public required SKColor Defeated { get; init; }

        public required SKColor Lock { get; init; }

        public required SKColor Pending { get; init; }

        public required bool Shadow { get; init; }

        /// <summary>夜色星空底图专用配色。</summary>
        public static Palette ForScene() => new()
        {
            Text = new SKColor(255, 255, 255),
            Defeated = new SKColor(255, 96, 96),
            Lock = new SKColor(255, 158, 62),
            Pending = new SKColor(226, 232, 240),
            Shadow = true,
        };
    }

    private sealed class DrawJob : IDisposable
    {
        private readonly SKCanvas canvas;
        private readonly ProgressSnapshot data;
        private readonly Palette palette;
        private readonly TextStyle title;
        private readonly TextStyle heading;
        private readonly TextStyle stat;
        private readonly TextStyle eventLabel;
        private readonly TextStyle eventDefeated;
        private readonly TextStyle eventPending;
        private readonly TextStyle eventAccent;
        private readonly TextStyle bossText;
        private readonly TextStyle bossDefeated;
        private readonly TextStyle bossLock;
        private readonly TextStyle signature;
        private readonly SKBitmap? lockIcon;
        private readonly Dictionary<string, SKBitmap?> bosses = new(StringComparer.OrdinalIgnoreCase);

        public DrawJob(SKBitmap bitmap, SKTypeface face, ProgressSnapshot data)
        {
            canvas = new SKCanvas(bitmap);
            this.data = data;
            palette = Palette.ForScene();

            title = new TextStyle(face, 64, palette.Text, palette.Shadow);
            heading = new TextStyle(face, 56, palette.Text, palette.Shadow);
            stat = new TextStyle(face, 44, palette.Defeated, palette.Shadow);
            eventLabel = new TextStyle(face, 44, palette.Text, palette.Shadow);
            eventDefeated = new TextStyle(face, 44, palette.Defeated, palette.Shadow);
            eventPending = new TextStyle(face, 44, palette.Pending, palette.Shadow);
            eventAccent = new TextStyle(face, 44, palette.Lock, palette.Shadow);
            bossText = new TextStyle(face, 52, palette.Text, palette.Shadow);
            bossDefeated = new TextStyle(face, 52, palette.Defeated, palette.Shadow);
            // 锁定文本（「明天05:44」）比其它标签长，单独用小一号字才放得进格子
            bossLock = new TextStyle(face, 44, palette.Lock, palette.Shadow);
            signature = new TextStyle(face, 24, palette.Text, palette.Shadow);

            // 注意：RenderKit.Asset 返回的是共享的缓存位图，绝对不能 using/Dispose，
            // 否则第二次渲染会拿到已释放对象，直接 0xC0000005 原生崩溃。
            SKBitmap? rawLock = RenderKit.Asset(LockItemIcon);
            lockIcon = rawLock is null ? null : RenderKit.Scale(rawLock, 38, 38);
        }

        public void Run()
        {
            DrawTitle();
            DrawEvents();
            DrawBosses();
        }

        // ── 标题 ────────────────────────────────────────────────────────────────

        private void DrawTitle()
        {
            string name = data.WorldName.Length > 0 ? data.WorldName : "未知世界";

            // 竖版画布宽 1080，64px 字号下 12 个字的世界名也只占 768px，
            // 不必像官方的 1920 横版那样给长名字单独缩字号。
            float nameWidth = title.Width(name);
            float nameX = MathF.Round((CardWidth - nameWidth) / 2f);
            title.Draw(canvas, name, nameX, TitleTop);

            string iconName = string.IsNullOrWhiteSpace(data.WorldIcon) ? "IconCrimson" : data.WorldIcon;
            SKBitmap? worldIcon = RenderKit.Asset($"images/world_icon/{iconName}.png")
                                  ?? RenderKit.Asset("images/world_icon/IconCrimson.png");
            if (worldIcon is not null)
            {
                // 官方会先抠掉世界图标的纯色底（transparent_back），否则会挡住背景
                using SKBitmap cleaned = worldIcon.Copy();
                TransparentBack(cleaned);
                using SKBitmap scaled = RenderKit.Scale(cleaned, 88, 88);
                RenderKit.DrawBitmap(canvas, scaled, nameX - scaled.Width - 12, TitleTop + 4);
            }

            heading.DrawCentered(canvas, "进度", CardWidth / 2f, HeadingTop);

            string[] list = data.ZenithWorld ? ZenithBosses : NormalBosses;
            int defeated = list.Count(Flag);
            stat.DrawRight(canvas, $"已击败 {defeated} / {list.Length}", CardWidth - Margin, StatTop);

            // 署名移到标题区右上角：Boss 网格排满后底部已经没有任何余量
            signature.DrawRight(
                canvas,
                $"By ak  {DateTime.Now:yyyy-MM-dd HH:mm}",
                CardWidth - Margin,
                SignatureTop);
        }

        // ── 入侵事件（两列 52px 行距，避免官方单列 24px 行距压字）──────────────

        private void DrawEvents()
        {
            (string Key, string Label)[] events =
            [
                ("Goblins", "哥布林军队"),
                ("Pirates", "海盗入侵"),
                ("Frost", "雪人军团"),
                ("Frost Moon", "霜月"),
                ("Pumpkin Moon", "南瓜月"),
                ("Old Ones Army", "旧日军团"),
            ];

            for (int index = 0; index < events.Length; index++)
            {
                int column = index % 2;
                int row = index / 2;
                float x = column == 0 ? EventColumnLeft : EventColumnRight;
                DrawEvent(events[index].Key, events[index].Label, x, EventTop + (row * EventRowHeight));
            }

            // 「四柱」未击败时要列出缺哪几根柱子，文本长度是其它事件的两倍多，
            // 塞进半宽列会溢出画布，因此单独占一整行。
            DrawEvent("Pillars", "四柱", EventColumnLeft, PillarsTop);
        }

        private void DrawEvent(string key, string label, float x, float y)
        {
            float cursor = x;
            SKBitmap? icon = Boss(key);
            if (icon is not null)
            {
                using SKBitmap scaled = RenderKit.Fit(icon, EventIconBox, EventIconBox);
                RenderKit.DrawBitmap(canvas, scaled, cursor, y + 3);
                cursor += EventIconBox + 10;
            }

            eventLabel.Draw(canvas, label + ":", cursor, y);
            cursor += eventLabel.Width(label + ":");

            if (string.Equals(key, "Old Ones Army", StringComparison.OrdinalIgnoreCase))
            {
                if (Flag("DD2InvasionT3"))
                {
                    eventAccent.Draw(canvas, "T3", cursor, y);
                }
                else if (Flag("DD2InvasionT2"))
                {
                    eventAccent.Draw(canvas, "T2", cursor, y);
                }
                else if (Flag("DD2InvasionT1"))
                {
                    eventAccent.Draw(canvas, "T1", cursor, y);
                }
                else
                {
                    eventPending.Draw(canvas, "未击败", cursor, y);
                }

                return;
            }

            if (string.Equals(key, "Pillars", StringComparison.OrdinalIgnoreCase))
            {
                if (Flag("Pillars"))
                {
                    eventDefeated.Draw(canvas, "已击败", cursor, y);
                }
                else
                {
                    List<string> pending = [];
                    if (!Flag("Tower Stardust"))
                    {
                        pending.Add("星尘");
                    }

                    if (!Flag("Tower Vortex"))
                    {
                        pending.Add("星璇");
                    }

                    if (!Flag("Tower Nebula"))
                    {
                        pending.Add("星云");
                    }

                    if (!Flag("Tower Solar"))
                    {
                        pending.Add("日耀");
                    }

                    eventPending.Draw(
                        canvas,
                        pending.Count == 0 ? "未击败" : $"未击败({string.Join(',', pending)})",
                        cursor,
                        y);
                }

                return;
            }

            if (Flag(key))
            {
                eventDefeated.Draw(canvas, "已击败", cursor, y);
            }
            else
            {
                eventPending.Draw(canvas, "未击败", cursor, y);
            }
        }

        // ── Boss 网格 ───────────────────────────────────────────────────────────

        private void DrawBosses()
        {
            string[] list = data.ZenithWorld ? ZenithBosses : NormalBosses;

            for (int index = 0; index < list.Length; index++)
            {
                float cellX = GridLeft + ((index % GridColumns) * GridColumnWidth);
                float cellY = GridTop + ((index / GridColumns) * GridRowHeight);
                DrawBoss(list[index], cellX, cellY);
            }

            // 20 个 Boss 排 3 列会剩出最后一格（3×7 = 21），拿它放统计徽章，避免右下角空一块
            DrawSummary();
        }

        /// <summary>网格最后一格的统计徽章（已击败 N / M）。</summary>
        private void DrawSummary()
        {
            string[] list = data.ZenithWorld ? ZenithBosses : NormalBosses;
            int defeated = list.Count(Flag);
            int slot = list.Length;

            float cellX = GridLeft + ((slot % GridColumns) * GridColumnWidth);
            float cellY = GridTop + ((slot / GridColumns) * GridRowHeight);
            float centerX = cellX + (GridColumnWidth / 2f);

            using TextStyle badge = new(RenderKit.Typeface!, 44, palette.Defeated, palette.Shadow);
            badge.DrawCentered(canvas, $"{defeated} / {list.Length}", centerX, cellY + 4);

            using TextStyle caption = new(RenderKit.Typeface!, 28, palette.Text, palette.Shadow);
            caption.DrawCentered(canvas, "已击败", centerX, cellY + 62);
        }

        private void DrawBoss(string name, float cellX, float cellY)
        {
            float centerX = cellX + (GridColumnWidth / 2f);
            float labelTop = cellY + IconBoxHeight + 8;

            SKBitmap? icon = Boss(name);
            if (icon is not null)
            {
                using SKBitmap scaled = RenderKit.Fit(icon, IconBoxWidth, IconBoxHeight);
                // 底部对齐：Boss 图标高矮差别极大（史莱姆 vs 骷髅王），顶部对齐会让
                // 「图标—标签」看起来脱节，底对齐后每行标签都在同一水平线上。
                RenderKit.DrawBitmap(
                    canvas,
                    scaled,
                    centerX - (scaled.Width / 2f),
                    cellY + IconBoxHeight - scaled.Height);
            }

            if (string.Equals(name, "Mechdusa", StringComparison.OrdinalIgnoreCase))
            {
                if (LockOf("Skeletron Prime") is string primeLock)
                {
                    DrawLocked(centerX, labelTop, primeLock);
                    return;
                }

                if (Flag("The Destroyer") && Flag("The Twins") && Flag("Skeletron Prime"))
                {
                    bossDefeated.DrawCentered(canvas, "已击败", centerX, labelTop);
                    return;
                }

                List<string> pending = [];
                if (!Flag("The Destroyer"))
                {
                    pending.Add("毁");
                }

                if (!Flag("Skeletron Prime"))
                {
                    pending.Add("骷");
                }

                if (!Flag("The Twins"))
                {
                    pending.Add("眼");
                }

                // 「未击败(毁,骷,眼)」在半宽列里会顶到相邻格，缩成「未:毁骷眼」
                bossText.DrawCentered(canvas, $"未:{string.Concat(pending)}", centerX, labelTop);
                return;
            }

            if (LockOf(name) is string locked)
            {
                DrawLocked(centerX, labelTop, locked);
                return;
            }

            if (Flag(name))
            {
                int kills = data.KillCounts.TryGetValue(name, out int count) ? count : 0;
                bossDefeated.DrawCentered(canvas, $"已击败({kills}次)", centerX, labelTop);
                return;
            }

            bossText.DrawCentered(canvas, "未击败", centerX, labelTop);
        }

        private void DrawLocked(float centerX, float labelTop, string text)
        {
            float textWidth = bossLock.Width(text);
            float iconWidth = lockIcon?.Width ?? 0;
            float gap = iconWidth > 0 ? 6 : 0;
            float startX = MathF.Round(centerX - ((textWidth + iconWidth + gap) / 2f));

            if (lockIcon is not null)
            {
                RenderKit.DrawBitmap(canvas, lockIcon, startX, labelTop + 5);
            }

            bossLock.Draw(canvas, text, startX + iconWidth + gap, labelTop);
        }

        // ── 工具 ────────────────────────────────────────────────────────────────

        private SKBitmap? Boss(string name)
        {
            if (bosses.TryGetValue(name, out SKBitmap? cached))
            {
                return cached;
            }

            SKBitmap? loaded = RenderKit.Asset($"images/bosses/{name}.png");
            bosses[name] = loaded;
            return loaded;
        }

        private bool Flag(string name)
        {
            return data.Process.TryGetValue(name, out bool value) && value;
        }

        private string? LockOf(string name)
        {
            return data.BossLock.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        /// <summary>
        /// 去掉世界图标贴图里按行变化的纯色底（对齐官方的 <c>transparent_back</c>）：逐行取前 10 列的颜色，
        /// 把该行中与它相同的像素置为全透明。
        /// </summary>
        private static void TransparentBack(SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            SKColor[] pixels = bitmap.Pixels;

            for (int column = 0; column < Math.Min(10, width); column++)
            {
                for (int row = 0; row < height; row++)
                {
                    SKColor reference = pixels[(row * width) + column];
                    for (int x = 0; x < width; x++)
                    {
                        int index = (row * width) + x;
                        if (pixels[index] == reference)
                        {
                            pixels[index] = reference.WithAlpha(0);
                        }
                    }
                }
            }

            bitmap.Pixels = pixels;
        }

        public void Dispose()
        {
            title.Dispose();
            heading.Dispose();
            stat.Dispose();
            eventLabel.Dispose();
            eventDefeated.Dispose();
            eventPending.Dispose();
            eventAccent.Dispose();
            bossText.Dispose();
            bossDefeated.Dispose();
            bossLock.Dispose();
            signature.Dispose();
            lockIcon?.Dispose();
            canvas.Dispose();
        }
    }
}
