using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using HeatTouls.Controls;
using HeatTouls.Core;
using Windows.Graphics;
using Windows.System;

namespace HeatTouls;

/// <summary>ダッシュボードのメインウィンドウ。Python 版 app.Dashboard の移植。</summary>
public sealed partial class MainWindow : Window
{
    private const int RefreshMs = 5000;

    // 一覧の左右に空ける余白。スクロールバー幅とその手前の間隔ぶん。
    private const double Gutter = 32;

    private static readonly (string Key, string Label)[] Ranges =
        [("all", "すべて"), ("30", "30日"), ("7", "7日")];

    private static readonly (string Key, string Label)[] Tabs =
        [("home", "ホーム"), ("overview", "概要"), ("apps", "ランキング")];

    private static readonly Dictionary<string, int?> RangeDays =
        new() { ["all"] = null, ["30"] = 30, ["7"] = 7 };

    // 期間ごとのヒートマップの行×列。行×列がそのまま表示日数になるので、
    // 期間の日数で割り切れる組を選んでいる(cols=null は幅いっぱいに詰める)。
    // ホームと概要は同じ形にして、並べたときに見比べられるようにする。
    private static readonly Dictionary<string, (int Rows, int? Cols)> HeatmapGrid =
        new() { ["all"] = (7, null), ["30"] = (3, 10), ["7"] = (1, 7) };

    private readonly Database _db;
    private readonly DispatcherTimer _timer = new();

    private SegmentedControl _tabs = null!;
    private SegmentedControl _ranges = null!;
    private readonly Dictionary<string, FrameworkElement> _pages = [];

    // ホーム
    private ScrollViewer _homeScroll = null!;
    private StackPanel _homeBody = null!;

    // 概要
    private DropDownButton _appPicker = null!;
    private TextBlock _appPickerText = null!;
    private TextBlock _appPeriod = null!;
    private TextBlock _footer = null!;
    private Heatmap _heatmap = null!;
    private HourlyChart _hourly = null!;
    private readonly Dictionary<string, StatCard> _cards = [];

    // ランキング
    private TextBlock _appsHeading = null!;
    private StackPanel _appRows = null!;

    // 一覧を組み直す必要があるかの判定に使う。数字ではなく「どのアプリがどの順に
    // 並ぶか」だけを見る。5秒ごとの自動更新で毎回組み直すとスクロール位置が飛ぶうえ、
    // アプリの数だけキャンバスを捨てて作ることになるため。
    private string? _homeSignature;
    private string? _appsSignature;
    private string? _overviewSignature;
    private string? _selectedApp;   // 概要タブで見ているアプリ

    private bool _minimizeOnClose;
    private bool _shuttingDown;

    /// <summary>トレイ以外の経路(閉じる=終了のとき)からの終了要求。</summary>
    public event Action? QuitRequested;

    public MainWindow(Database db, bool minimizeOnClose = false, string title = "HeatTouls")
    {
        _db = db;
        _minimizeOnClose = minimizeOnClose;

        InitializeComponent();
        Title = title;
        Root.Background = Theme.BgBrush;
        Root.RequestedTheme = ElementTheme.Dark;

        ConfigureWindow();
        Build();
        Refresh();

        _timer.Interval = TimeSpan.FromMilliseconds(RefreshMs);
        _timer.Tick += (_, _) =>
        {
            try
            {
                Refresh();
            }
            catch (Exception)
            {
                // 一時的なDBロック等で更新ループを止めない
            }
        };
        _timer.Start();

        AppWindow.Closing += OnClosing;
    }

    // --- ウィンドウ ------------------------------------------------------------

    private void ConfigureWindow()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(680 * scale)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 860;
            presenter.PreferredMinimumHeight = 560;
        }

        // 標準タイトルバーを本体の暗い背景に合わせる。
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = Theme.Bg;
        bar.ForegroundColor = Theme.Text;
        bar.InactiveBackgroundColor = Theme.Bg;
        bar.InactiveForegroundColor = Theme.Muted;
        bar.ButtonBackgroundColor = Theme.Bg;
        bar.ButtonForegroundColor = Theme.Text;
        bar.ButtonInactiveBackgroundColor = Theme.Bg;
        bar.ButtonInactiveForegroundColor = Theme.Muted;
        bar.ButtonHoverBackgroundColor = Theme.CardHover;
        bar.ButtonHoverForegroundColor = Theme.Text;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HeatTouls.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shuttingDown)
        {
            return;
        }
        if (_minimizeOnClose)
        {
            // トレイ常駐中は閉じるボタンで隠すだけ。
            args.Cancel = true;
            HideDashboard();
            return;
        }
        args.Cancel = true;
        QuitRequested?.Invoke();
    }

    public void ShowDashboard()
    {
        AppWindow.Show();
        Activate();
        Refresh();
        _timer.Start();
    }

    public void HideDashboard()
    {
        AppWindow.Hide();
        // 誰も見ていない画面を更新し続けない。ここを止めないと、隠したあとも5秒ごとに
        // 一覧が組み直され、下の ReleaseContent で手放したぶんがすぐ作り直される。
        _timer.Stop();
        ReleaseContent();
    }

    /// <summary>
    /// 一覧が抱えているものを捨てて、メモリを返す。
    ///
    /// ウィンドウを隠してもビジュアルツリーはそのまま残る。ここでのツリーは計測対象の
    /// アプリ1つにつき Win2D の CanvasControl (それぞれが自前のスワップチェーンを持つ)
    /// なので、トレイに居る間ずっと抱えておく価値はない。ShowDashboard の Refresh が
    /// 組み直す。
    /// </summary>
    private void ReleaseContent()
    {
        try
        {
            ClearHome();
            _appRows.Children.Clear();

            // Refresh は署名が変わっていなければ組み直さないので、ここで消しておかないと
            // next ShowDashboard で空のまま戻ってくる。
            _homeSignature = null;
            _appsSignature = null;
            _overviewSignature = null;
        }
        catch (Exception)
        {
            // 画面の後始末で計測まで巻き添えにしない。
        }

        MemoryTrim.Release();
    }

    /// <summary>
    /// ホームの行を捨てる。Unloaded 頼みだとウィンドウを隠している間に外したぶんの
    /// キャンバスが取り残されるので、明示的に解放してから外す。
    /// </summary>
    private void ClearHome()
    {
        foreach (var child in _homeBody.Children)
        {
            (child as AppHeatmapRow)?.Release();
        }
        _homeBody.Children.Clear();
    }

    /// <summary>App.Quit から呼ばれる後始末。</summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        _timer.Stop();
        Close();
    }

    // --- 組み立て --------------------------------------------------------------

    private void Build()
    {
        _tabs = new SegmentedControl(Tabs, padx: 13);
        _tabs.SelectionChanged += OnTabChanged;
        TabHost.Content = _tabs;

        _ranges = new SegmentedControl(Ranges);
        _ranges.SelectionChanged += _ => OnRangeChanged();
        RangeHost.Content = _ranges;

        _pages["home"] = BuildHome();
        _pages["overview"] = BuildOverview();
        _pages["apps"] = BuildApps();
        foreach (var (key, page) in _pages)
        {
            page.Visibility = key == "home" ? Visibility.Visible : Visibility.Collapsed;
            PageHost.Children.Add(page);
        }

        // Ctrl+R で再集計、Esc でウィンドウを閉じる(トレイへ)
        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.R, VirtualKeyModifiers.Control,
            () => Refresh()));
        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Escape, VirtualKeyModifiers.None,
            OnEscape));
    }

    private static KeyboardAccelerator Accelerator(VirtualKey key, VirtualKeyModifiers modifiers,
                                                   Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return accelerator;
    }

    private void OnEscape()
    {
        if (_minimizeOnClose)
        {
            HideDashboard();
        }
        else
        {
            QuitRequested?.Invoke();
        }
    }

    /// <summary>ホームタブ: アプリごとのヒートマップだけを縦に並べた一覧。</summary>
    private FrameworkElement BuildHome()
    {
        // 右のスクロールバーと釣り合うように、中身の左右へ同じだけ余白を取る。
        _homeBody = new StackPanel { Margin = new Thickness(Gutter, 0, Gutter, 0) };
        _homeScroll = new ScrollViewer
        {
            Content = _homeBody,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        return _homeScroll;
    }

    /// <summary>概要タブ: アプリを1つ選んで、その詳細を見る画面。</summary>
    private FrameworkElement BuildOverview()
    {
        var page = new StackPanel();

        // 上部: 対象アプリの選択
        _appPickerText = new TextBlock
        {
            Text = "—",
            FontFamily = Theme.Family,
            FontSize = Theme.FontBody,
            Foreground = Theme.TextBrush,
        };
        _appPicker = new DropDownButton
        {
            Content = _appPickerText,
            Background = Theme.CardBrush,
            BorderBrush = Theme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 4, 12, 4),
            Flyout = new MenuFlyout(),
        };

        _appPeriod = new TextBlock
        {
            FontFamily = Theme.Family,
            FontSize = Theme.FontSmall,
            Foreground = Theme.FaintBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Text = "対象",
                    FontFamily = Theme.Family,
                    FontSize = Theme.FontSmall,
                    Foreground = Theme.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                },
                _appPicker,
            },
        };

        var picker = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_appPeriod, 1);
        picker.Children.Add(left);
        picker.Children.Add(_appPeriod);
        page.Children.Add(picker);

        // 統計カード 8 枚 (4列 × 2行)
        var grid = new Grid();
        for (var col = 0; col < 4; col++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        (string Key, string Label)[] specs =
        [
            ("total", "合計稼働時間"),
            ("share", "全体に占める割合"),
            ("sessions", "セッション"),
            ("active_days", "アクティブ日数"),
            ("average", "1日あたり"),
            ("streak", "現在の連続日数"),
            ("longest", "最長連続日数"),
            ("peak", "ピーク時間"),
        ];
        for (var index = 0; index < specs.Length; index++)
        {
            var (key, label) = specs[index];
            var card = new StatCard(label)
            {
                Margin = new Thickness(index % 4 == 0 ? 0 : 5, index < 4 ? 0 : 8, 0, 0),
            };
            Grid.SetRow(card, index / 4);
            Grid.SetColumn(card, index % 4);
            grid.Children.Add(card);
            _cards[key] = card;
        }
        page.Children.Add(grid);

        // 見出しも凡例も置かず、背景に直接ヒートマップを敷く
        _heatmap = new Heatmap(rows: 7, axis: true, legend: true, weekdays: true)
        {
            Margin = new Thickness(0, 20, 0, 0),
        };
        page.Children.Add(_heatmap);

        // そのアプリを1日のどの時間帯に使っているか
        _hourly = new HourlyChart { Margin = new Thickness(0, 16, 0, 0) };
        page.Children.Add(_hourly);

        _footer = new TextBlock
        {
            FontFamily = Theme.Family,
            FontSize = Theme.FontSmall,
            Foreground = Theme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        page.Children.Add(_footer);

        return new ScrollViewer
        {
            Content = page,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    /// <summary>ランキングタブ: よく使ったアプリの一覧だけ。</summary>
    private FrameworkElement BuildApps()
    {
        _appsHeading = new TextBlock
        {
            Text = "よく使ったアプリ",
            FontFamily = Theme.Family,
            FontSize = Theme.FontSection,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Theme.TextBrush,
            Margin = new Thickness(0, 0, 0, 9),
        };

        _appRows = new StackPanel { Margin = new Thickness(Gutter, 0, Gutter, 0) };
        var scroll = new ScrollViewer
        {
            Content = _appRows,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);

        var page = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        page.Children.Add(_appsHeading);
        page.Children.Add(scroll);
        return page;
    }

    // --- 状態 ------------------------------------------------------------------

    private void OnTabChanged(string key)
    {
        foreach (var (name, page) in _pages)
        {
            page.Visibility = name == key ? Visibility.Visible : Visibility.Collapsed;
        }
        Refresh();
    }

    private void OnRangeChanged()
    {
        // 期間が変わればどのタブも作り直す必要がある
        _homeSignature = null;
        _appsSignature = null;
        _overviewSignature = null;
        Refresh();
    }

    /// <summary>アプリ一覧の行から、そのアプリの詳細(概要タブ)へ飛ぶ。</summary>
    private void OpenApp(string name)
    {
        _selectedApp = name;
        _overviewSignature = null;
        _tabs.Select("overview");
    }

    /// <summary>
    /// 現在表示中のタブだけを再構築する。ホームタブはアプリ数ぶん描くので、
    /// 毎回すべてのタブを更新すると重い。
    /// </summary>
    public void Refresh()
    {
        var rangeKey = _ranges.Value;
        switch (_tabs.Value)
        {
            case "home":
                RefreshHome(rangeKey);
                break;
            case "overview":
                RefreshOverview(rangeKey);
                break;
            default:
                RefreshApps(rangeKey);
                break;
        }
    }

    // --- ホーム ----------------------------------------------------------------

    private void RefreshHome(string rangeKey)
    {
        var apps = Stats.AppDaily(_db, RangeDays[rangeKey]);
        var end = Stats.Today();
        var (rows, cols) = HeatmapGrid[rangeKey];

        // 顔ぶれと並びが同じなら、行はそのままで数字とヒートマップだけ差し替える。
        var signature = rangeKey + "|" + string.Join(",", apps.Select(a => a.Name));
        if (signature == _homeSignature)
        {
            for (var index = 0; index < apps.Count && index < _homeBody.Children.Count; index++)
            {
                (_homeBody.Children[index] as AppHeatmapRow)?.Update(apps[index], end, rows, cols);
            }
            return;
        }
        var previousRange = _homeSignature?.Split('|', 2)[0];
        _homeSignature = signature;

        ClearHome();
        if (apps.Count == 0)
        {
            _homeBody.Children.Add(EmptyLabel());
            _homeScroll.ChangeView(null, 0, null, true);
            return;
        }

        // 一覧全体で色がかぶらないように色相を割り当てる
        var hues = Theme.AssignHues(apps.Select(a => a.Name));

        foreach (var app in apps)
        {
            _homeBody.Children.Add(new AppHeatmapRow(
                app, end, rows, cols, hues[app.Name], a => OpenApp(a.Name))
            {
                Margin = new Thickness(0, 0, 0, 14),
            });
        }

        // 期間を切り替えた時だけ先頭に戻す。計測による更新では今の位置を保つ。
        if (previousRange != rangeKey)
        {
            _homeScroll.ChangeView(null, 0, null, true);
        }
    }

    private static TextBlock EmptyLabel() => new()
    {
        Text = "まだ記録がありません。",
        FontFamily = Theme.Family,
        FontSize = Theme.FontBody,
        Foreground = Theme.MutedBrush,
        Margin = new Thickness(0, 20, 0, 20),
    };

    // --- 概要(アプリ詳細) --------------------------------------------------------

    private void RefreshOverview(string rangeKey)
    {
        var days = RangeDays[rangeKey];
        var names = Stats.AppNames(_db, days);

        // 選択中のアプリがこの期間に無ければ、一番使っているアプリに寄せる
        if (_selectedApp is null || !names.Contains(_selectedApp))
        {
            _selectedApp = names.Count > 0 ? names[0] : null;
        }

        var signature = $"{rangeKey}|{_selectedApp}|{string.Join(",", names)}";
        if (signature != _overviewSignature)
        {
            _overviewSignature = signature;
            RebuildAppMenu(names);
        }

        _appPickerText.Text = _selectedApp is null ? "—" : Fmt.Truncate(_selectedApp, 30);
        var (rows, cols) = HeatmapGrid[rangeKey];

        if (_selectedApp is null)
        {
            foreach (var card in _cards.Values)
            {
                card.SetValue("—");
            }
            _heatmap.SetData(new Dictionary<string, double>(), rows, cols);
            _hourly.SetData(new Dictionary<int, double>());
            _appPeriod.Text = "";
            _footer.Text = "まだ記録がありません。";
            return;
        }

        var detail = Stats.Detail(_db, _selectedApp, days);
        _cards["total"].SetValue(Fmt.Duration(detail.TotalSeconds));
        _cards["share"].SetValue($"{detail.Share * 100:F0}%");
        _cards["sessions"].SetValue(Fmt.Count(detail.SessionCount));
        _cards["active_days"].SetValue(Fmt.Days(detail.ActiveDays));
        _cards["average"].SetValue(Fmt.Duration(detail.AverageSeconds));
        _cards["streak"].SetValue(Fmt.Days(detail.CurrentStreak));
        _cards["longest"].SetValue(Fmt.Days(detail.LongestStreak));
        _cards["peak"].SetValue(Fmt.HourLabel(detail.PeakHour));

        _heatmap.SetData(detail.Daily, rows, cols,
            palette: Theme.HeatRamp(Theme.AppHue(_selectedApp)));
        _hourly.SetData(detail.Hourly);
        _appPeriod.Text = PeriodText(detail);
        _footer.Text = FooterText(detail);
    }

    private void RebuildAppMenu(IReadOnlyList<string> names)
    {
        var flyout = new MenuFlyout();
        foreach (var name in names)
        {
            var item = new MenuFlyoutItem { Text = Fmt.Truncate(name, 40) };
            var captured = name;
            item.Click += (_, _) =>
            {
                _selectedApp = captured;
                Refresh();
            };
            flyout.Items.Add(item);
        }
        if (names.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "(記録なし)", IsEnabled = false });
        }
        _appPicker.Flyout = flyout;
    }

    private static string PeriodText(AppDetail detail)
    {
        if (detail.FirstDay is null)
        {
            return "";
        }
        var first = detail.FirstDay.Replace('-', '/');
        if (detail.FirstDay == detail.LastDay)
        {
            return first;
        }
        return $"{first} 〜 {detail.LastDay?.Replace('-', '/')}";
    }

    private static string FooterText(AppDetail detail)
    {
        if (detail.TotalSeconds <= 0)
        {
            return $"{detail.Name} はこの期間に記録がありません。";
        }
        var movies = detail.TotalSeconds / (2 * 3600);
        return $"{detail.Name} を合計 {Fmt.DurationLong(detail.TotalSeconds)} — " +
               $"長編映画（2時間）約{movies:N0}本分。" +
               $" 全体の{detail.Share * 100:F0}%を占めています。";
    }

    // --- ランキング --------------------------------------------------------------

    private void RefreshApps(string rangeKey)
    {
        var apps = Stats.AppBreakdown(_db, RangeDays[rangeKey]);
        _appsHeading.Text = apps.Count > 0
            ? $"よく使ったアプリ · {apps.Count}"
            : "よく使ったアプリ";

        var peak = apps.Count > 0 ? apps.Max(a => a.Seconds) : 0.0;
        if (peak <= 0)
        {
            peak = 1.0;
        }

        // ホームと同じで、顔ぶれと並びが同じなら中身だけ差し替える。
        var signature = rangeKey + "|" + string.Join(",", apps.Select(a => a.Name));
        if (signature == _appsSignature)
        {
            for (var index = 0; index < apps.Count && index < _appRows.Children.Count; index++)
            {
                (_appRows.Children[index] as AppUsageRow)?.Update(apps[index], peak);
            }
            return;
        }
        _appsSignature = signature;

        _appRows.Children.Clear();
        if (apps.Count == 0)
        {
            _appRows.Children.Add(EmptyLabel());
            return;
        }

        for (var index = 0; index < apps.Count; index++)
        {
            _appRows.Children.Add(new AppUsageRow(
                apps[index], peak, index + 1, a => OpenApp(a.Name))
            {
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
    }
}
