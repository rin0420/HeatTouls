namespace HeatTouls.Core;

/// <summary>コマンドライン引数。Python 版 main.py の argparse と同じ形。</summary>
public sealed class CliOptions
{
    public bool Minimized { get; private set; }
    public bool TrackOnly { get; private set; }
    public bool NoTray { get; private set; }
    public bool Demo { get; private set; }
    public string? Db { get; private set; }
    public string? Autostart { get; private set; }   // "on" | "off" | "status"
    public bool Help { get; private set; }
    public string? Error { get; private set; }

    public const string Usage = """
        HeatTouls — PC稼働時間の計測と可視化

          HeatTouls.exe                  計測しながらダッシュボードを表示(トレイ常駐)
          HeatTouls.exe --minimized      ウィンドウを出さずトレイのみで起動
          HeatTouls.exe --track-only     GUIを出さず計測だけ行う
          HeatTouls.exe --no-tray        タスクトレイに常駐しない
          HeatTouls.exe --demo           サンプルデータ入りの別DBでUIを確認する
          HeatTouls.exe --db <PATH>      使用するDBファイルを指定する
          HeatTouls.exe --autostart on|off|status
                                         ログオン時の自動起動を設定する
        """;

    public static CliOptions Parse(string[] argv)
    {
        var options = new CliOptions();
        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--minimized":
                    options.Minimized = true;
                    break;
                case "--track-only":
                    options.TrackOnly = true;
                    break;
                case "--no-tray":
                    options.NoTray = true;
                    break;
                case "--demo":
                    options.Demo = true;
                    break;
                case "--db":
                    if (i + 1 >= argv.Length)
                    {
                        options.Error = "--db にはファイルパスが必要です。";
                        return options;
                    }
                    options.Db = argv[++i];
                    break;
                case "--autostart":
                    if (i + 1 >= argv.Length || argv[i + 1] is not ("on" or "off" or "status"))
                    {
                        options.Error = "--autostart には on / off / status のいずれかを指定します。";
                        return options;
                    }
                    options.Autostart = argv[++i];
                    break;
                case "-h" or "--help":
                    options.Help = true;
                    break;
                default:
                    options.Error = $"不明な引数です: {argv[i]}";
                    return options;
            }
        }
        return options;
    }
}
