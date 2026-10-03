using SkiaSharp;

namespace CaiBotWindy.Services;

/// <summary>
/// 一只字体 + 一种颜色，负责把 PIL 的「左上角锚点」换算成 Skia 的基线坐标
/// （<c>baseline = top - font.Metrics.Ascent</c>），并可选带描影以在图鉴/背景图上保持可读。
/// </summary>
public sealed class TextStyle : IDisposable
{
    private static readonly SKColor ShadowColor = new(0, 0, 0, 150);

    private readonly SKFont font;
    private readonly SKPaint paint;
    private readonly SKPaint? shadowPaint;
    private readonly float baselineOffset;

    public TextStyle(SKTypeface face, float size, SKColor color, bool shadow = false)
        : this(face, size, color, shadow ? ShadowColor : null)
    {
    }

    public TextStyle(SKTypeface face, float size, SKColor color, SKColor? shadow)
    {
        font = new SKFont(face, size);
        paint = new SKPaint { Color = color, IsAntialias = true };
        shadowPaint = shadow is null ? null : new SKPaint { Color = shadow.Value, IsAntialias = true };
        baselineOffset = -font.Metrics.Ascent;
        Size = size;
        LineHeight = font.Metrics.Descent - font.Metrics.Ascent;
    }

    /// <summary>字号。</summary>
    public float Size { get; }

    /// <summary>一行的高度（ascent 到 descent）。</summary>
    public float LineHeight { get; }

    /// <summary><paramref name="top"/> 为文字左上角纵坐标（与 PIL 的 la 锚点一致）。</summary>
    public void Draw(SKCanvas canvas, string text, float x, float top)
    {
        float baseline = top + baselineOffset;
        if (shadowPaint is not null)
        {
            // 描边（而不是单侧投影）：进度卡片的标签会压在山脉、岩浆等深浅不一的背景上，
            // 单侧阴影挡不住，一圈描边才能保证任何底色上都读得清。
            for (int i = 0; i < OutlineOffsets.Length; i++)
            {
                (float dx, float dy) = OutlineOffsets[i];
                canvas.DrawText(text, x + dx, baseline + dy, font, shadowPaint);
            }
        }

        canvas.DrawText(text, x, baseline, font, paint);
    }

    private static readonly (float Dx, float Dy)[] OutlineOffsets =
    [
        (-2f, 0f), (2f, 0f), (0f, -2f), (0f, 2f),
        (-2f, -2f), (2f, -2f), (-2f, 2f), (2f, 2f),
    ];

    /// <summary>以 <paramref name="centerX"/> 为水平中心绘制（向上取整避免半像素导致的重影）。</summary>
    public void DrawCentered(SKCanvas canvas, string text, float centerX, float top)
    {
        Draw(canvas, text, MathF.Round(centerX - (Width(text) / 2f)), top);
    }

    /// <summary>以 <paramref name="right"/> 为右边界绘制。</summary>
    public void DrawRight(SKCanvas canvas, string text, float right, float top)
    {
        Draw(canvas, text, MathF.Round(right - Width(text)), top);
    }

    public float Width(string text)
    {
        return font.MeasureText(text);
    }

    public void Dispose()
    {
        font.Dispose();
        paint.Dispose();
        shadowPaint?.Dispose();
    }
}
