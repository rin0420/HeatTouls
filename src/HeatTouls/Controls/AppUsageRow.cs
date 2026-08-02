using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using HeatTouls.Core;

namespace HeatTouls.Controls;

/// <summary>ランキング1行。カラーマーク・名前・進捗バー・時間をまとめて出す。</summary>
public sealed class AppUsageRow : UserControl
{
    private const double Pad = 13;

    private readonly Grid _track;
    private readonly TextBlock _duration;
    private readonly TextBlock _sessions;

    private AppUsage _app;

    public AppUsageRow(AppUsage app, double peak, int? rank = null, Action<AppUsage>? onClick = null)
    {
        _app = app;
        var color = Theme.AppAccent(app.Name);
        var grid = new Grid
        {
            Margin = new Thickness(Pad, 0, Pad, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },   // 順位
                new ColumnDefinition { Width = GridLength.Auto },   // カラーマーク
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },   // 時間 / セッション
            },
        };

        if (rank is not null)
        {
            var rankText = new TextBlock
            {
                Text = rank.Value.ToString(),
                FontFamily = Theme.Family,
                FontSize = Theme.FontSmall,
                Foreground = Theme.FaintBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 20,
            };
            Grid.SetColumn(rankText, 0);
            grid.Children.Add(rankText);
        }

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(dot, 1);
        grid.Children.Add(dot);

        // 名前とバーを縦に積む。バーの長さは一番使っているアプリを 1.0 とした比率。
        var ratio = Ratio(app.Seconds, peak);
        _track = new Grid
        {
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = new SolidColorBrush(Theme.BarTrack),
            Margin = new Thickness(0, 6, 14, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) },
            },
        };
        var fill = new Border
        {
            CornerRadius = new CornerRadius(2.5),
            Background = new SolidColorBrush(color),
        };
        Grid.SetColumn(fill, 0);
        _track.Children.Add(fill);

        var main = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = Fmt.Truncate(app.Name, 34),
                    FontFamily = Theme.Family,
                    FontSize = Theme.FontBody,
                    Foreground = Theme.TextBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                _track,
            },
        };
        Grid.SetColumn(main, 2);
        grid.Children.Add(main);

        _duration = new TextBlock
        {
            Text = Fmt.DurationLong(app.Seconds),
            FontFamily = Theme.Family,
            FontSize = Theme.FontValueSm,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Theme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _sessions = new TextBlock
        {
            Text = SessionText(app.Sessions),
            FontFamily = Theme.Family,
            FontSize = Theme.FontSmall,
            Foreground = Theme.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var right = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _duration, _sessions },
        };
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);

        var card = new Border
        {
            Height = 58,
            Background = Theme.CardBrush,
            BorderBrush = Theme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.Radius),
            Child = grid,
        };
        Content = card;

        if (onClick is not null)
        {
            // ハンドラは作り直さないので、クリック先は今表示しているアプリを都度見る。
            PointerEntered += (_, _) =>
            {
                card.Background = new SolidColorBrush(Theme.CardHover);
                ProtectedCursor = Cursors.Hand;
            };
            PointerExited += (_, _) =>
            {
                card.Background = Theme.CardBrush;
                ProtectedCursor = Cursors.Arrow;
            };
            PointerPressed += (_, _) => onClick(_app);
        }
    }

    /// <summary>
    /// 数字とバーだけを差し替える。順位と色はアプリの顔ぶれと並びで決まるので、
    /// それが変わらない限り作り直す必要がない。
    /// </summary>
    public void Update(AppUsage app, double peak)
    {
        _app = app;
        _duration.Text = Fmt.DurationLong(app.Seconds);
        _sessions.Text = SessionText(app.Sessions);

        var ratio = Ratio(app.Seconds, peak);
        _track.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
        _track.ColumnDefinitions[1].Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    /// <summary>0でもバーが見えるように下限を置く。</summary>
    private static double Ratio(double seconds, double peak) =>
        Math.Max(0.02, Math.Min(1.0, seconds / Math.Max(peak, 1.0)));

    private static string SessionText(int sessions) => $"{sessions}セッション";
}
