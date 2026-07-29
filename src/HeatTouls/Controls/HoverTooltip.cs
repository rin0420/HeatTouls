using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace HeatTouls.Controls;

/// <summary>
/// マウス位置に出す小さな黒いラベル。Python 版 widgets.Tooltip の置き換え。
/// WinUI の ToolTipService は位置が固定なので、Popup で自前に持つ。
/// </summary>
public sealed class HoverTooltip
{
    private readonly Popup _popup;
    private readonly TextBlock _text;

    public HoverTooltip()
    {
        _text = new TextBlock
        {
            FontFamily = Theme.Family,
            FontSize = Theme.FontSmall,
            Foreground = Theme.TextBrush,
        };
        _popup = new Popup
        {
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Theme.TooltipBg),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                Child = _text,
            },
        };
    }

    /// <summary>position は element 内のローカル座標。</summary>
    public void Show(UIElement element, string text, Point position)
    {
        if (element.XamlRoot is null)
        {
            return;
        }

        _text.Text = text;
        _popup.XamlRoot = element.XamlRoot;

        // ローカル座標をウィンドウ座標へ移してから、カーソルの少し右下に置く。
        var origin = element.TransformToVisual(null).TransformPoint(position);
        _popup.HorizontalOffset = origin.X + 14;
        _popup.VerticalOffset = origin.Y + 18;
        _popup.IsOpen = true;
    }

    public void Hide() => _popup.IsOpen = false;
}
