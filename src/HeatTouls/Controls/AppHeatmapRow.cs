using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using HeatTouls.Core;

namespace HeatTouls.Controls;

/// <summary>ホームタブの1行: アプリ名 + 合計時間 + そのアプリのヒートマップ。</summary>
public sealed class AppHeatmapRow : UserControl
{
    public AppHeatmapRow(AppDailyUsage app, DateOnly end, int rows = 7, int? cols = null,
                         int? hue = null, Action<AppDailyUsage>? onClick = null)
    {
        var appHue = hue ?? Theme.AppHue(app.Name);
        var accent = Theme.Accent(appHue);

        // アプリの色を示す丸いマーク。ヒートマップの色と対応している。
        var chip = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };

        var name = new TextBlock
        {
            Text = Fmt.Truncate(app.Name, 44),
            FontFamily = Theme.Family,
            FontSize = Theme.FontBody,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var total = new TextBlock
        {
            Text = Fmt.DurationLong(app.Seconds),
            FontFamily = Theme.Family,
            FontSize = Theme.FontSmall,
            Foreground = Theme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { chip, name },
        };

        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(total, 1);
        header.Children.Add(left);
        header.Children.Add(total);

        // マスの大きさは概要タブと揃える。濃さはアプリごとに正規化するので、
        // 使用量の少ないアプリでも形が見える。
        var heatmap = new Heatmap(rows: rows, axis: cols is null, weekdays: true);
        heatmap.SetData(app.Daily, rows, cols, end, Theme.HeatRamp(appHue));

        Content = new StackPanel { Children = { header, heatmap } };

        if (onClick is not null)
        {
            left.PointerPressed += (_, _) => onClick(app);
            left.PointerEntered += (_, _) =>
            {
                name.Foreground = new SolidColorBrush(accent);
                ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                    Microsoft.UI.Input.InputSystemCursorShape.Hand);
            };
            left.PointerExited += (_, _) =>
            {
                name.Foreground = Theme.TextBrush;
                ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                    Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            };
        }
    }
}
