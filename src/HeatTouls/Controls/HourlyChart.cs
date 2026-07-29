using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using HeatTouls.Core;
using Windows.Foundation;

namespace HeatTouls.Controls;

/// <summary>0時〜23時の時間帯別の稼働時間を並べた棒グラフ。</summary>
public sealed class HourlyChart : UserControl
{
    private const double Gap = 4;
    private const double LabelH = 16;

    private readonly CanvasControl _canvas = new();
    private readonly HoverTooltip _tooltip = new();
    private readonly Dictionary<int, (double X0, double X1)> _bars = [];

    private IReadOnlyDictionary<int, double> _hourly = new Dictionary<int, double>();

    public HourlyChart(double height = 104)
    {
        Height = height;
        _canvas.Draw += OnDraw;
        Content = _canvas;

        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => _tooltip.Hide();
        SizeChanged += (_, _) => _canvas.Invalidate();
        Unloaded += (_, _) =>
        {
            _tooltip.Hide();
            _canvas.RemoveFromVisualTree();
        };
    }

    public void SetData(IReadOnlyDictionary<int, double> hourly)
    {
        _hourly = hourly;
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        _bars.Clear();

        var width = sender.ActualWidth;
        var height = sender.ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var ds = args.DrawingSession;
        var area = height - LabelH;
        var barWidth = Math.Max(3, (width - Gap * 23) / 24);
        var step = barWidth + Gap;
        var peak = _hourly.Count > 0 ? _hourly.Values.Max() : 0.0;
        var busiest = _hourly.Count > 0
            ? _hourly.Aggregate((a, b) => b.Value > a.Value ? b : a).Key
            : -1;
        var radius = (float)Math.Min(3.0, barWidth / 2);

        for (var hour = 0; hour < 24; hour++)
        {
            _hourly.TryGetValue(hour, out var seconds);
            var x = hour * step;

            ds.FillRoundedRectangle(
                new Rect(x, 0, barWidth, area), radius, radius, Theme.BarTrack);

            if (peak <= 0 || seconds <= 0)
            {
                continue;
            }
            var barHeight = Math.Max(3, Math.Round(area * (seconds / peak)));
            var color = hour == busiest ? Theme.Heat[4] : Theme.Bar;
            ds.FillRoundedRectangle(
                new Rect(x, area - barHeight, barWidth, barHeight), radius, radius, color);
            _bars[hour] = (x, x + barWidth);
        }

        using var format = CanvasText.Format(Theme.FontTiny);
        for (var hour = 0; hour < 24; hour += 3)
        {
            CanvasText.Draw(ds, hour.ToString(), hour * step + barWidth / 2, area + LabelH / 2,
                Theme.Faint, format, HAnchor.Center, VAnchor.Center);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        foreach (var (hour, (x0, x1)) in _bars)
        {
            if (point.X >= x0 && point.X <= x1)
            {
                _hourly.TryGetValue(hour, out var seconds);
                _tooltip.Show(this, $"{hour}時台 · {Fmt.DurationLong(seconds)}", point);
                return;
            }
        }
        _tooltip.Hide();
    }
}
