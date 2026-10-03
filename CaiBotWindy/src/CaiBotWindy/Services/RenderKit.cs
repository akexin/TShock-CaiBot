using System.Runtime.InteropServices;
using SkiaSharp;
using Windy.SDK;

namespace CaiBotWindy.Services;

/// <summary>
/// 卡片渲染公共设施：SkiaSharp 原生库预加载、素材读取与缓存、缩放/裁剪、面板绘制、PNG 输出。
/// <para>进度卡片（<see cref="ProgressRenderer"/>）与背包卡片（<see cref="BagRenderer"/>）共用。</para>
/// <para>素材根目录 = Windy 运行目录下的 <c>Asserts</c>，与 HTTP 路由 <c>/assets/</c> 同源。</para>
/// </summary>
public static class RenderKit
{
    /// <summary>卡片统一使用的字体（与官方 CaiBotLite 一致）。</summary>
    public const string FontFile = "fonts/LXGWWenKaiMono-Medium.ttf";

    private static readonly Dictionary<string, SKBitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static string assetRoot = "";
    private static bool nativeReady;
    private static string? unavailableReason = "未初始化";

    /// <summary>素材根目录（Windy 运行目录下的 <c>Asserts</c>）。</summary>
    public static string AssetRoot => assetRoot;

    /// <summary>已加载的中文字体；为 null 表示渲染不可用。</summary>
    public static SKTypeface? Typeface { get; private set; }

    /// <summary>渲染是否可用（原生库 + 字体都就绪）。</summary>
    public static bool Ready => Typeface is not null && assetRoot.Length > 0;

    /// <summary>返回不可用原因；可用时为 null。</summary>
    public static string? UnavailableReason => Ready ? null : unavailableReason ?? "素材未就绪";

    /// <summary>配置素材目录；缺失字体或原生库时渲染会被禁用（调用方自动退回文本输出）。</summary>
    public static void Configure(string root)
    {
        assetRoot = root;
        Typeface = null;
        Cache.Clear();

        if (!EnsureNativeLibrary(out string nativeError))
        {
            unavailableReason = nativeError;
            return;
        }

        string fontPath = Path.Combine(root, FontFile);
        if (!File.Exists(fontPath))
        {
            unavailableReason = $"缺少字体文件 {fontPath}";
            return;
        }

        try
        {
            Typeface = SKTypeface.FromFile(fontPath);
            unavailableReason = Typeface is null ? "字体加载失败" : null;
        }
        catch (Exception ex)
        {
            Typeface = null;
            unavailableReason = $"字体加载异常: {ex.Message}";
        }
    }

    /// <summary>
    /// 预加载 SkiaSharp 的原生库。
    /// <para>插件是被 Windy 动态加载的，宿主 Windy.exe 的 deps.json 里没有 libSkiaSharp，
    /// 常规 RID 探测找不到它；这里按绝对路径先 Load 进进程，之后 DllImport 就能直接命中已加载模块。</para>
    /// </summary>
    private static bool EnsureNativeLibrary(out string error)
    {
        error = "";
        if (nativeReady)
        {
            return true;
        }

        string[] candidates =
        [
            "libSkiaSharp",
            Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.dll"),
            Path.Combine(AppContext.BaseDirectory, "Plugins", "libSkiaSharp.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libSkiaSharp.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x86", "native", "libSkiaSharp.dll"),
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                if (candidate.Contains(Path.DirectorySeparatorChar) && !File.Exists(candidate))
                {
                    continue;
                }

                NativeLibrary.Load(candidate);
                nativeReady = true;
                return true;
            }
            catch (Exception ex)
            {
                error = $"无法加载 libSkiaSharp（{Path.GetFileName(candidate)}）: {ex.Message}";
            }
        }

        error = error.Length > 0 ? error : "找不到 libSkiaSharp.dll，请把它放到机器人运行目录";
        return false;
    }

    // ── 素材 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 按相对素材根的路径读取图片并缓存（读不到返回 null，不会抛异常）。
    /// <para><b>返回的是共享实例，调用方绝对不能 Dispose</b>——释放后第二次命中缓存会拿到已销毁的对象，
    /// 在 <c>SKImage.FromBitmap</c> 处直接 0xC0000005 原生崩溃。需要改图先 <c>Copy()</c>。</para>
    /// </summary>
    public static SKBitmap? Asset(string relativePath)
    {
        if (string.IsNullOrEmpty(assetRoot) || string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        // 缓存被人误 Dispose 时自愈重载，避免整个进程崩掉
        if (Cache.TryGetValue(relativePath, out SKBitmap? cached)
            && cached is not null
            && cached.Handle != IntPtr.Zero)
        {
            return cached;
        }

        SKBitmap? loaded = LoadImage(Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Cache[relativePath] = loaded;
        return loaded;
    }

    /// <summary>素材是否存在（图鉴图标用；不做缓存，补图后无需重启）。</summary>
    public static bool AssetExists(string relativePath)
    {
        return !string.IsNullOrEmpty(assetRoot)
               && !string.IsNullOrEmpty(relativePath)
               && File.Exists(Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static SKBitmap? LoadImage(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return SKBitmap.Decode(fullPath);
        }
        catch (Exception ex)
        {
            Message.Yellow($"[RenderKit] 素材读取失败 {fullPath}: {ex.Message}");
            return null;
        }
    }

    // ── 画布 ────────────────────────────────────────────────────────────────────

    public static SKBitmap NewCanvas(int width, int height)
    {
        SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    public static SKBitmap Scale(SKBitmap source, int width, int height, bool crisp = false)
    {
        SKBitmap target = NewCanvas(Math.Max(1, width), Math.Max(1, height));
        using SKCanvas canvas = new(target);
        using SKPaint paint = new() { IsAntialias = !crisp };
        using SKImage image = SKImage.FromBitmap(source);
        SKSamplingOptions sampling = crisp
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(0, 0, width, height),
            sampling,
            paint);
        return target;
    }

    /// <summary>等比缩放到不超过 <paramref name="maxWidth"/>×<paramref name="maxHeight"/>（默认不放大小图）。</summary>
    public static SKBitmap Fit(SKBitmap source, int maxWidth, int maxHeight, bool allowUpscale = false)
    {
        float ratio = Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height);
        if (!allowUpscale && ratio > 1f)
        {
            ratio = 1f;
        }

        int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        return width == source.Width && height == source.Height ? source.Copy() : Scale(source, width, height);
    }

    /// <summary>以「覆盖」方式把图片铺满 <paramref name="width"/>×<paramref name="height"/>（居中裁剪）。</summary>
    public static void DrawCover(SKCanvas canvas, SKBitmap source, int width, int height, bool crisp = false)
    {
        float ratio = Math.Max((float)width / source.Width, (float)height / source.Height);
        float drawWidth = source.Width * ratio;
        float drawHeight = source.Height * ratio;
        float left = (width - drawWidth) / 2f;
        float top = (height - drawHeight) / 2f;

        using SKPaint paint = new() { IsAntialias = !crisp };
        using SKImage image = SKImage.FromBitmap(source);
        SKSamplingOptions sampling = crisp
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(left, top, left + drawWidth, top + drawHeight),
            sampling,
            paint);
    }

    /// <summary>生成一张以素材为底的卡片画布；素材缺失时返回 null。</summary>
    public static SKBitmap? CreateCard(string backgroundRelativePath, int width, int height, bool crisp = false)
    {
        SKBitmap? background = Asset(backgroundRelativePath);
        if (background is null)
        {
            return null;
        }

        SKBitmap canvasBitmap = NewCanvas(width, height);
        using (SKCanvas canvas = new(canvasBitmap))
        {
            DrawCover(canvas, background, width, height, crisp);
        }

        return canvasBitmap;
    }

    public static void DrawBitmap(SKCanvas canvas, SKBitmap bitmap, float x, float y)
    {
        using SKPaint paint = new() { IsAntialias = true };
        canvas.DrawBitmap(bitmap, x, y, paint);
    }

    /// <summary>半透明圆角面板（给文字/图标提供稳定底色，提升可读性）；<paramref name="border"/> 非空时再描一圈边。</summary>
    public static void Panel(SKCanvas canvas, SKRect rect, float radius, SKColor color, SKColor? border = null)
    {
        using SKPaint paint = new()
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(rect, radius, radius, paint);

        if (border is null)
        {
            return;
        }

        using SKPaint stroke = new()
        {
            Color = border.Value,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        canvas.DrawRoundRect(rect, radius, radius, stroke);
    }

    public static byte[]? Encode(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    /// <summary>把图片画成圆形/圆角（列表头像用），按 <paramref name="alpha"/> 控制透明度。</summary>
    public static void DrawScaled(SKCanvas canvas, SKBitmap bitmap, SKRect destination)
    {
        using SKPaint paint = new() { IsAntialias = true };
        using SKImage image = SKImage.FromBitmap(bitmap);
        SKSamplingOptions sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, bitmap.Width, bitmap.Height),
            destination,
            sampling,
            paint);
    }
}
