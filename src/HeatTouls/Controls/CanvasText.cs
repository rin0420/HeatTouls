using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace HeatTouls.Controls;

internal enum HAnchor { Left, Center, Right }

internal enum VAnchor { Top, Center, Bottom }

/// <summary>
/// tkinter の create_text(anchor=...) と同じ感覚で Win2D にテキストを置くヘルパ。
/// 十分に大きな矩形を基準点からずらして置き、その中で寄せることで実現する。
/// </summary>
internal static class CanvasText
{
    private const double Span = 4000;

    public static CanvasTextFormat Format(double size, bool bold = false) => new()
    {
        FontFamily = "Segoe UI",
        FontSize = (float)size,
        FontWeight = bold
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    public static void Draw(
        CanvasDrawingSession ds, string text, double x, double y, Color color,
        CanvasTextFormat format, HAnchor h = HAnchor.Left, VAnchor v = VAnchor.Top)
    {
        format.HorizontalAlignment = h switch
        {
            HAnchor.Left => CanvasHorizontalAlignment.Left,
            HAnchor.Center => CanvasHorizontalAlignment.Center,
            _ => CanvasHorizontalAlignment.Right,
        };
        format.VerticalAlignment = v switch
        {
            VAnchor.Top => CanvasVerticalAlignment.Top,
            VAnchor.Center => CanvasVerticalAlignment.Center,
            _ => CanvasVerticalAlignment.Bottom,
        };

        var left = h switch
        {
            HAnchor.Left => x,
            HAnchor.Center => x - Span / 2,
            _ => x - Span,
        };
        var top = v switch
        {
            VAnchor.Top => y,
            VAnchor.Center => y - Span / 2,
            _ => y - Span,
        };

        ds.DrawText(text, new Rect(left, top, Span, Span), color, format);
    }

    /// <summary>文字列の描画幅。ラベルが重なるかの判定に使う。</summary>
    public static double Measure(ICanvasResourceCreator device, string text, CanvasTextFormat format)
    {
        using var layout = new CanvasTextLayout(device, text, format, 0, 0);
        return layout.LayoutBounds.Width;
    }
}
