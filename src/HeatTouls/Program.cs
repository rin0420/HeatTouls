using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using HeatTouls.Core;

namespace HeatTouls;

/// <summary>
/// エントリポイント。GUI を出す前に CLI の用事(--autostart, --track-only)を片付ける。
/// XAML が生成する Main は DISABLE_XAML_GENERATED_MAIN で無効にしている。
/// </summary>
public static class Program
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    [STAThread]
    public static int Main(string[] argv)
    {
        // WinExe なのでコンソールを持たない。コマンドラインから起動された場合は
        // 親のコンソールへ書けるように繋ぎ、そうでなければダイアログで伝える。
        AttachConsole(AttachParentProcess);

        var options = CliOptions.Parse(argv);
        if (options.Error is not null)
        {
            Notify($"{options.Error}\n\n{CliOptions.Usage}");
            return 2;
        }
        if (options.Help)
        {
            Notify(CliOptions.Usage);
            return 0;
        }

        if (options.Db is not null)
        {
            Environment.SetEnvironmentVariable("HEATTOULS_DB", options.Db);
        }
        if (options.Autostart is not null)
        {
            return HandleAutostart(options.Autostart);
        }
        if (options.TrackOnly)
        {
            return RunTrackerOnly();
        }

        App.Options = options;
        return RunGui();
    }

    private static int RunGui()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static int HandleAutostart(string mode)
    {
        switch (mode)
        {
            case "on":
                Notify($"自動起動を登録しました:\n{Autostart.Enable()}");
                break;
            case "off":
                Notify(Autostart.Disable()
                    ? "自動起動を解除しました。"
                    : "自動起動は登録されていません。");
                break;
            default:
                var current = Autostart.Status();
                Notify(current is not null ? $"登録済み:\n{current}" : "自動起動は登録されていません。");
                break;
        }
        return 0;
    }

    private static int RunTrackerOnly()
    {
        using var tracker = new Tracker();
        tracker.Start();
        Console.WriteLine("計測中です。Ctrl+C で終了します。");

        using var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;   // 既定の即時終了を止めて、書き出しを済ませてから抜ける
            done.Set();
        };
        done.Wait();

        tracker.Stop();
        tracker.Join(TimeSpan.FromSeconds(5));
        return 0;
    }

    /// <summary>
    /// コマンドラインからは標準出力へ、exeの直起動時はダイアログで伝える。
    /// </summary>
    internal static void Notify(string message)
    {
        // パイプやファイルへリダイレクトされている場合はコンソールハンドルが
        // 取れないので、その時も標準出力へ書く。ダイアログはダブルクリック起動
        // (どこにも出力先が無い) のときだけ。
        if (GetConsoleWindow() != IntPtr.Zero || Console.IsOutputRedirected)
        {
            Console.WriteLine(message);
            return;
        }
        MessageBoxW(IntPtr.Zero, message, "HeatTouls", 0x40 /* MB_ICONINFORMATION */);
    }
}
