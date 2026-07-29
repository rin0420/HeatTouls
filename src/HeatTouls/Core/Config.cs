namespace HeatTouls.Core;

/// <summary>設定値とパス解決。</summary>
public static class Config
{
    public const string AppName = "HeatTouls";
    public const string LegacyAppName = "toulstudio";   // 旧名。保存先の引き継ぎに使う

    // --- 計測パラメータ ---------------------------------------------------
    public const double PollSeconds = 1.0;          // 前面ウィンドウを見に行く間隔
    public const double FlushSeconds = 20.0;        // メモリ上の集計をDBへ書き出す間隔
    public const double IdleThreshold = 180.0;      // 何秒無操作ならアイドル扱いにするか
    public const double SessionGap = 300.0;         // この秒数以上間隔が空いたらセッションを切る
    public const double MinSessionSeconds = 10.0;   // これ未満のセッションは記録しない
    public const double MinActiveDaySeconds = 60.0; // 「アクティブな日」とみなす1日の最低秒数

    /// <summary>
    /// 計測対象から除外する実行ファイル名。ロック画面やIME、シェル自身など
    /// 「ユーザーが使っているアプリ」ではないもの。
    /// </summary>
    public static readonly IReadOnlySet<string> IgnoredExes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lockapp.exe",
            "logonui.exe",
            "textinputhost.exe",
            "shellexperiencehost.exe",
            "searchhost.exe",
            "searchapp.exe",
            "startmenuexperiencehost.exe",
            "applicationframehost.exe",  // UWPホスト。中身のPIDに解決できなかった場合のみ該当
            "idle",
        };

    /// <summary>HEATTOULS_* を見る。旧名の TOULSTUDIO_* も引き続き受け付ける。</summary>
    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable($"HEATTOULS_{name}");
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }
        value = Environment.GetEnvironmentVariable($"TOULSTUDIO_{name}");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>データ保存先。HEATTOULS_DATA_DIR で上書きできる。</summary>
    public static string DataDir()
    {
        var over = Env("DATA_DIR");
        if (over is not null)
        {
            Directory.CreateDirectory(over);
            return over;
        }

        var baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var path = Path.Combine(baseDir, AppName);
        if (!Directory.Exists(path))
        {
            // 旧名(toulstudio)で貯めた記録があれば、そのまま引き継ぐ
            var legacy = Path.Combine(baseDir, LegacyAppName);
            if (Directory.Exists(legacy))
            {
                try
                {
                    Directory.Move(legacy, path);
                }
                catch (IOException)
                {
                    path = legacy;   // 使用中などで移せない場合は旧フォルダを使い続ける
                }
                catch (UnauthorizedAccessException)
                {
                    path = legacy;
                }
            }
        }
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>DBファイルのパス。HEATTOULS_DB で上書きできる。</summary>
    public static string DbPath()
    {
        var over = Env("DB");
        if (over is not null)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(over));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            return over;
        }
        return Path.Combine(DataDir(), "usage.db");
    }
}
