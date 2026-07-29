using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace HeatTouls.Controls;

/// <summary>
/// 『すべて / 30日 / 7日』のような切り替えボタン群。
/// 選択中の項目だけ角丸の下地を敷く。
/// </summary>
public sealed class SegmentedControl : UserControl
{
    private const double Height_ = 26;
    private const double Gap = 3;

    private readonly List<(string Key, Border Pill, TextBlock Label)> _items = [];

    /// <summary>選択が変わったときに、新しいキーで呼ばれる。</summary>
    public event Action<string>? SelectionChanged;

    public string Value { get; private set; }

    public SegmentedControl(IReadOnlyList<(string Key, string Label)> options, double padx = 12)
    {
        Value = options[0].Key;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, label) in options)
        {
            var text = new TextBlock
            {
                Text = label,
                FontFamily = Theme.Family,
                FontSize = Theme.FontSeg,
                Foreground = new SolidColorBrush(Theme.SegIdleFg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border
            {
                Height = Height_,
                CornerRadius = new CornerRadius(Height_ / 2),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(padx, 0, padx, 0),
                Margin = new Thickness(_items.Count == 0 ? 0 : Gap, 0, 0, 0),
                Child = text,
            };

            var captured = key;
            pill.PointerPressed += (_, _) => Select(captured);
            pill.PointerEntered += (_, e) => ProtectedCursor =
                Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
            pill.PointerExited += (_, _) => ProtectedCursor =
                Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

            _items.Add((key, pill, text));
            panel.Children.Add(pill);
        }

        Content = panel;
        Paint();
    }

    public void Select(string key, bool notify = true)
    {
        if (key == Value)
        {
            return;
        }
        Value = key;
        Paint();
        if (notify)
        {
            SelectionChanged?.Invoke(key);
        }
    }

    private void Paint()
    {
        foreach (var (key, pill, label) in _items)
        {
            var active = key == Value;
            pill.Background = new SolidColorBrush(
                active ? Theme.SegActiveBg : Microsoft.UI.Colors.Transparent);
            label.Foreground = new SolidColorBrush(
                active ? Theme.SegActiveFg : Theme.SegIdleFg);
        }
    }
}
