using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using HeatTouls.Core;
using Windows.Foundation;
using Windows.UI;

namespace HeatTouls.Controls;

/// <summary>
/// 日別の稼働時間を、隙間なく詰めたマス目で表すヒートマップ。
///
/// 曜日を無視して古い順に上から下へ埋め、埋まったら次の列へ進む。
/// 左上が最も古く、右下が今日。全部のマスが実在する日付なので端から端まで詰まる。
/// weekdays=true のときだけ「行=曜日」に揃え、右端を今週の土曜まで伸ばす。
/// </summary>
public sealed class Heatmap : UserControl
{
    private const double Gap = 3;
    private const double MinCell = 8;
    private const double MaxCellFixed = 34;   // 表示日数が決まっているとき
    private const double TargetCell = 15;     // 幅いっぱいに詰めるときの目標サイズ

    private const double AxisH = 15;          // 上に置く月ラベルの高さ
    private const double LegendH = 17;        // 下に置く凡例の高さ
    private const double WeekdayW = 28;       // 左に置く曜日ラベルの幅

    // 上から 日月火水木金土。全部書くと詰まるので月・水・金だけ出す。
    private static readonly (int Row, string Label)[] WeekdayLabels = [(1, "月"), (3, "水"), (5, "金")];

    private static readonly string[] WeekdayJa = ["月", "火", "水", "木", "金", "土", "日"];

    private readonly CanvasControl _canvas = new();
    private readonly HoverTooltip? _tooltip;

    private IReadOnlyDictionary<string, double> _daily = new Dictionary<string, double>();
    private int _rows;
    private int? _cols;
    private Color[] _palette = Theme.Heat;
    private DateOnly _end = Stats.Today();
    private readonly bool _axis;
    private readonly bool _legend;
    private readonly bool _weekdays;

    private Geometry? _geometry;
    private bool _released;

    private readonly record struct Geometry(
        double Cell, double Step, int Count, DateOnly Start,
        double Offset, int Rows, double Top, int Cols, bool ShowWeekdays);

    public Heatmap(int rows = 7, bool axis = false, bool legend = false,
                   bool weekdays = false, bool tooltips = true)
    {
        _rows = rows;
        _axis = axis;
        _legend = legend;
        _weekdays = weekdays;

        _canvas.Draw += OnDraw;
        Content = _canvas;
        Height = rows * 18;

        if (tooltips)
        {
            _tooltip = new HoverTooltip();
            PointerMoved += OnPointerMoved;
            PointerExited += (_, _) => _tooltip.Hide();
        }

        SizeChanged += (_, _) => Relayout();
        Unloaded += (_, _) => Release();
    }

    /// <summary>
    /// CanvasControl とその裏のスワップチェーンを手放す。
    ///
    /// Win2D のキャンバスはデバイス消失イベントに自分を繋いだままにするので、親から
    /// 外しただけでは解放されない。Unloaded 頼みにすると、ウィンドウを隠している間に
    /// 一覧を捨てたぶんが取り残されるため、捨てる側から直接呼べるようにしておく。
    /// </summary>
    public void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        _tooltip?.Hide();
        _canvas.RemoveFromVisualTree();
    }

    /// <summary>
    /// cols を省くと幅いっぱいに詰める(列数は幅から決まる)。
    /// 期間を指定して表示する場合は rows と cols を明示する。行×列がそのまま
    /// 表示日数になるので、7日なら1x7、30日なら3x10 のように割り切れる組にする。
    /// </summary>
    public void SetData(IReadOnlyDictionary<string, double> daily, int? rows = null,
                        int? cols = null, DateOnly? end = null, Color[]? palette = null)
    {
        _daily = daily;
        if (rows is > 0)
        {
            _rows = rows.Value;
        }
        _cols = cols;
        _palette = palette ?? Theme.Heat;
        _end = end ?? Stats.Today();
        Relayout();
    }

    /// <summary>日曜を先頭(0)としたときの行番号。</summary>
    private static int WeekRow(DateOnly day) => (int)day.DayOfWeek;

    private int Level(double seconds, double peak)
    {
        if (seconds <= 0 || peak <= 0)
        {
            return 0;
        }
        var ratio = seconds / peak;
        if (ratio >= 0.75) return 4;
        if (ratio >= 0.5) return 3;
        if (ratio >= 0.25) return 2;
        return 1;
    }

    /// <summary>幅から幾何を決め、必要な高さを自分に設定する。</summary>
    private void Relayout()
    {
        var width = ActualWidth;
        if (width <= 1)
        {
            return;
        }

        // 曜日ラベルは1列が7日ぶん = 1行が1曜日、のときだけ意味を持つ
        var showWeekdays = _weekdays && _rows == 7;
        var labelW = showWeekdays ? WeekdayW : 0;
        var avail = Math.Max(1, width - labelW);

        int cols;
        double cell;
        if (_cols is int fixedCols and > 0)
        {
            // 表示日数が決まっている場合。マスが大きくなりすぎないよう頭打ちにし、
            // 幅が余ったら中央に寄せる。
            cols = fixedCols;
            cell = Math.Min(MaxCellFixed, (avail - Gap * (cols - 1)) / cols);
        }
        else
        {
            // 幅いっぱいに詰める場合。目標サイズから列数を決め、そのあとマスを
            // 伸縮させて端から端まできっちり収める。
            cols = Math.Max(1, (int)Math.Round((avail + Gap) / (TargetCell + Gap), MidpointRounding.ToEven));
            cell = (avail - Gap * (cols - 1)) / cols;
            if (cell < MinCell && cols > 1)
            {
                cols = Math.Max(1, (int)((avail + Gap) / (MinCell + Gap)));
                cell = (avail - Gap * (cols - 1)) / cols;
            }
        }
        cell = Math.Max(1.0, cell);
        var step = cell + Gap;
        var offset = labelW + Math.Max(0.0, (avail - (cols * step - Gap)) / 2);

        // 行数は固定。高さに合わせて行を増やすと、記録のある期間よりずっと長い
        // 範囲を描くことになり、灰色のマスばかりが増えてしまう。
        var top = _axis ? AxisH : 0;
        var gridH = _rows * step - Gap;

        var count = cols * _rows;
        var end = _end;
        if (showWeekdays)
        {
            // 右端を今週の土曜まで伸ばす。マス数が7の倍数になるので、先頭が
            // 必ず日曜になり、全部のマスが埋まったまま「行=曜日」が揃う。
            end = end.AddDays(6 - WeekRow(end));
        }
        var start = end.AddDays(-(count - 1));

        _geometry = new Geometry(cell, step, count, start, offset, _rows, top, cols, showWeekdays);

        var height = Math.Round(top + gridH + (_legend ? LegendH : 0));
        if (Math.Abs(Height - height) > 0.5)
        {
            Height = height;
        }
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_geometry is not { } g)
        {
            return;
        }
        var ds = args.DrawingSession;
        var radius = (float)(g.Cell * Theme.CellRadiusRatio);
        var peak = _daily.Count > 0 ? _daily.Values.Max() : 0.0;

        for (var index = 0; index < g.Count; index++)
        {
            var day = g.Start.AddDays(index);
            _daily.TryGetValue(day.ToString(Stats.DayFmt), out var seconds);
            var color = _palette[Level(seconds, peak)];

            var x = g.Offset + index / g.Rows * g.Step;
            var y = g.Top + index % g.Rows * g.Step;
            ds.FillRoundedRectangle(
                new Rect(x, y, g.Cell, g.Cell), radius, radius, color);
        }

        if (g.ShowWeekdays)
        {
            DrawWeekdays(ds, g);
        }
        if (_axis)
        {
            DrawAxis(ds, sender, g);
        }
        if (_legend)
        {
            DrawLegend(ds, sender.ActualWidth, g.Top + (g.Rows * g.Step - Gap));
        }
    }

    /// <summary>左端に曜日を書く。全部書くと詰まるので月・水・金だけ。</summary>
    private static void DrawWeekdays(CanvasDrawingSession ds, Geometry g)
    {
        using var format = CanvasText.Format(Theme.FontTiny);
        foreach (var (row, label) in WeekdayLabels)
        {
            if (row >= g.Rows)
            {
                continue;
            }
            CanvasText.Draw(ds, label, WeekdayW - 8, g.Top + row * g.Step + g.Cell / 2,
                Theme.Muted, format, HAnchor.Right, VAnchor.Center);
        }
    }

    /// <summary>
    /// 列の上に月の変わり目を書く。1列が7日ぶんなので、その列で月が変わったら
    /// 列の左端に月名を置く。隣のラベルと重なる場合は飛ばす。
    /// </summary>
    private void DrawAxis(CanvasDrawingSession ds, CanvasControl sender, Geometry g)
    {
        using var format = CanvasText.Format(Theme.FontTiny);

        // 先頭の列はたいてい月の途中から始まる。ここにラベルを置くと次の月の
        // ラベルと重なって潰れるので、月の記録だけして描かない。
        var lastMonth = g.Start.Month;
        var lastX = -999.0;

        for (var col = 1; col < g.Cols; col++)
        {
            var day = g.Start.AddDays(col * g.Rows);
            if (day.Month == lastMonth)
            {
                continue;
            }
            lastMonth = day.Month;

            var x = g.Offset + col * g.Step;
            var text = $"{day.Month}月";
            if (x - lastX < CanvasText.Measure(sender, text, format) + 8)
            {
                continue;
            }
            lastX = x;
            CanvasText.Draw(ds, text, x, AxisH / 2, Theme.Muted, format,
                HAnchor.Left, VAnchor.Center);
        }
    }

    /// <summary>右下に「少 ▪▪▪▪ 多」を置く。色の向きを迷わせないため。</summary>
    private void DrawLegend(CanvasDrawingSession ds, double width, double top)
    {
        const double size = 9;
        const double gap = 3;

        using var format = CanvasText.Format(Theme.FontTiny);
        var y = top + LegendH / 2;
        var x = width;

        CanvasText.Draw(ds, "多", x, y, Theme.Faint, format, HAnchor.Right, VAnchor.Center);
        x -= 16;
        for (var i = _palette.Length - 1; i >= 0; i--)
        {
            ds.FillRectangle(new Rect(x - size, y - size / 2, size, size), _palette[i]);
            x -= size + gap;
        }
        CanvasText.Draw(ds, "少", x - 3, y, Theme.Faint, format, HAnchor.Right, VAnchor.Center);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_geometry is not { } g || _tooltip is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this).Position;
        var localX = point.X - g.Offset;
        var col = (int)Math.Floor(localX / g.Step);
        var row = (int)Math.Floor(point.Y / g.Step);

        // マスの間の隙間や、グリッドの外に乗っている時は何も出さない
        if (localX < 0 || localX % g.Step > g.Cell || point.Y % g.Step > g.Cell || row >= g.Rows || row < 0)
        {
            _tooltip.Hide();
            return;
        }

        var index = col * g.Rows + row;
        if (index < 0 || index >= g.Count)
        {
            _tooltip.Hide();
            return;
        }

        var day = g.Start.AddDays(index);
        if (day > Stats.Today())
        {
            // 週の区切りを揃えるため今週の残りまで描いている。まだ来ていない日。
            _tooltip.Hide();
            return;
        }

        _daily.TryGetValue(day.ToString(Stats.DayFmt), out var seconds);
        var weekday = WeekdayJa[((int)day.DayOfWeek + 6) % 7];
        var text = $"{day:yyyy/MM/dd}（{weekday}） · " +
                   (seconds > 0 ? Fmt.DurationLong(seconds) : "記録なし");
        _tooltip.Show(this, text, new Point(point.X, point.Y));
    }
}
