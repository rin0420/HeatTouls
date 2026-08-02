using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using HeatTouls.Core;
using Windows.UI;

namespace HeatTouls.Controls;

/// <summary>ホームタブの1行: アプリ名 + 合計時間 + そのアプリのヒートマップ。</summary>
public sealed class AppHeatmapRow : UserControl
{
    private readonly TextBlock _total;
    private readonly Heatmap _heatmap;
    private readonly Color[] _palette;

    private AppDailyUsage _app;

    public AppHeatmapRow(AppDailyUsage app, DateOnly end, int rows = 7, int? cols = null,
                         int? hue = null, Action<AppDailyUsage>? onClick = null)
    {
        _app = app;
        var appHue = hue ?? Theme.AppHue(app.Name);
        var accent = Theme.Accent(appHue);
        _palette = Theme.HeatRamp(appHue);

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

        _total = new TextBlock
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
        Grid.SetColumn(_total, 1);
        header.Children.Add(left);
        header.Children.Add(_total);

        // マスの大きさは概要タブと揃える。濃さはアプリごとに正規化するので、
        // 使用量の少ないアプリでも形が見える。
        _heatmap = new Heatmap(rows: rows, axis: cols is null, weekdays: true);
        _heatmap.SetData(app.Daily, rows, cols, end, _palette);

        Content = new StackPanel { Children = { header, _heatmap } };

        if (onClick is not null)
        {
            // ハンドラは作り直さないので、クリック先は今表示しているアプリを都度見る。
            left.PointerPressed += (_, _) => onClick(_app);
            left.PointerEntered += (_, _) =>
            {
                name.Foreground = new SolidColorBrush(accent);
                ProtectedCursor = Cursors.Hand;
            };
            left.PointerExited += (_, _) =>
            {
                name.Foreground = Theme.TextBrush;
                ProtectedCursor = Cursors.Arrow;
            };
        }
    }

    /// <summary>
    /// 数字とヒートマップだけを差し替える。
    ///
    /// 5秒ごとの自動更新で行ごと作り直すと、アプリの数だけ CanvasControl (それぞれが
    /// 自前のスワップチェーンを持つ) を捨てて作ることになる。顔ぶれが変わっていない
    /// 限りは行を使い回す。
    /// </summary>
    public void Update(AppDailyUsage app, DateOnly end, int rows, int? cols)
    {
        _app = app;
        _total.Text = Fmt.DurationLong(app.Seconds);
        _heatmap.SetData(app.Daily, rows, cols, end, _palette);
    }

    /// <summary>一覧から外すときに、キャンバスの後始末を確実に済ませる。</summary>
    public void Release() => _heatmap.Release();
}
