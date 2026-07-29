using Microsoft.UI.Xaml;
using HeatTouls.Core;

namespace HeatTouls;

/// <summary>
/// アプリ本体。計測スレッド・ダッシュボード・トレイ常駐の面倒を見る。
/// </summary>
public partial class App : Application
{
    /// <summary>Program.Main が解析した引数。</summary>
    public static CliOptions Options { get; set; } = CliOptions.Parse([]);

    private Tracker? _tracker;
    private Database? _db;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _quitting;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var demo = Options.Demo;
        var title = demo ? "HeatTouls (デモデータ)" : "HeatTouls";

        if (demo)
        {
            // 実データに影響を出さないよう、サンプルは別ファイルへ書く。
            var demoPath = Path.Combine(Config.DataDir(), "demo.db");
            _db = Database.Open(demoPath);
            if (_db.IsEmpty())
            {
                Demo.Seed(_db);
            }
        }
        else
        {
            _tracker = new Tracker();
            _tracker.Start();
            _db = Database.Open();
        }

        // デモはUI確認用なので常駐しない。閉じる=終了。
        var useTray = !demo && !Options.NoTray;
        _window = new MainWindow(_db, minimizeOnClose: useTray, title: title);
        _window.QuitRequested += Quit;

        if (useTray)
        {
            _tray = new TrayIcon(
                "HeatTouls — 稼働時間を計測中",
                onShow: () => _window.ShowDashboard(),
                onQuit: Quit);
        }

        if (Options.Minimized && useTray)
        {
            _window.HideDashboard();
        }
        else
        {
            _window.Activate();
        }
    }

    /// <summary>トレイの「終了」やウィンドウの終了要求から呼ばれる。</summary>
    public void Quit()
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;

        _tray?.Dispose();
        _tray = null;

        if (_tracker is not null)
        {
            _tracker.Stop();
            _tracker.Join(TimeSpan.FromSeconds(5));
            _tracker.Dispose();
            _tracker = null;
        }

        _window?.Shutdown();
        _db?.Dispose();
        _db = null;

        Exit();
    }
}
