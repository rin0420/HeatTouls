using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HeatTouls.Controls;

/// <summary>『ラベル + 大きな数値』のカード1枚。</summary>
public sealed class StatCard : UserControl
{
    private const double Pad = 13;

    private readonly TextBlock _value;

    public StatCard(string label, string value = "—")
    {
        _value = new TextBlock
        {
            Text = value,
            FontFamily = Theme.Family,
            FontSize = Theme.FontValue,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Pad, 0, Pad, Pad - 2),
        };

        var caption = new TextBlock
        {
            Text = label,
            FontFamily = Theme.Family,
            FontSize = Theme.FontLabel,
            Foreground = Theme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Pad, Pad - 2, Pad, 0),
        };

        Content = new Border
        {
            Height = 74,
            Background = Theme.CardBrush,
            BorderBrush = Theme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.Radius),
            Child = new Grid { Children = { caption, _value } },
        };
    }

    public void SetValue(string value)
    {
        if (_value.Text != value)
        {
            _value.Text = value;
        }
    }
}
